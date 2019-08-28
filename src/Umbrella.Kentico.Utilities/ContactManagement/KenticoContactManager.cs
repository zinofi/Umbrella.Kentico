using System;
using System.Collections.Generic;
using System.Linq;
using CMS.ContactManagement;
using CMS.ContactManagement.Internal;
using CMS.Membership;
using Microsoft.Extensions.Logging;
using Microsoft.Owin;
using Umbrella.Kentico.Utilities.ContactManagement.Abstractions;
using Umbrella.Kentico.Utilities.ContactManagement.Options;
using Umbrella.Kentico.Utilities.Users.Abstractions;
using Umbrella.Utilities;
using Umbrella.Utilities.Extensions;

namespace Umbrella.Kentico.Utilities.ContactManagement
{
	public class KenticoContactManager : IKenticoContactManager
	{
		private readonly ILogger _log;
        private readonly KenticoContactManagerOptions _options;
        private readonly IKenticoUserNameNormalizer _kenticoUserNameNormalizer;
		private readonly Lazy<IContactProcessingChecker> _contactProcessingChecker;
		private readonly Lazy<IContactCreator> _contactCreator;
		private readonly Lazy<IContactRelationAssigner> _contactRelationAssigner;
		private readonly Lazy<IContactPersistentStorage> _contactPersistentStorage;
		private readonly Lazy<IContactMergeService> _contactMergeService;

		public KenticoContactManager(
			ILogger<KenticoContactManager> logger,
            KenticoContactManagerOptions options,
			IKenticoUserNameNormalizer kenticoUserNameNormalizer,
			Lazy<IContactProcessingChecker> contactProcessingChecker,
			Lazy<IContactCreator> contactCreator,
			Lazy<IContactRelationAssigner> contactRelationAssigner,
			Lazy<IContactPersistentStorage> contactPersistentStorage,
			Lazy<IContactMergeService> contactMergeService)
		{
			_log = logger;
            _options = options;
            _kenticoUserNameNormalizer = kenticoUserNameNormalizer;
			_contactProcessingChecker = contactProcessingChecker;
			_contactCreator = contactCreator;
			_contactRelationAssigner = contactRelationAssigner;
			_contactPersistentStorage = contactPersistentStorage;
			_contactMergeService = contactMergeService;
		}

		public void Merge(string userName)
		{
			Guard.ArgumentNotNullOrWhiteSpace(userName, nameof(userName));

			try
			{
				string kenticoUserName = _kenticoUserNameNormalizer.Normalize(userName);

				if (!_contactProcessingChecker.Value.CanProcessContactInCurrentContext())
					return;

				UserInfo userInfo = UserInfoProvider.GetUserInfo(kenticoUserName);

				if (userInfo == null)
					return;

				if (string.IsNullOrWhiteSpace(userInfo.Email))
					throw new KenticoContactException($"The specified user with userName: {kenticoUserName} does not have an email address specified.");

				// Try and find a contact with a matching email for the current user
				ContactInfo currentContact = ContactInfoProvider.GetContactInfo(userInfo.Email);

				// Find the contact based on the current request. This is read from the CurrentContact cookie internally.
				ContactInfo cookieContact = _contactPersistentStorage.Value.GetPersistentContact();

                // If a contact based on the CurrentContact cookie cannot be found, create a new anonymous one.
                // Such a scenario may present itself if the cookie has been tampered with or the contact no longer exists in Kentico.
                if (cookieContact == null)
                    cookieContact = _contactCreator.Value.CreateAnonymousContact();

                if (currentContact?.ContactEmail == cookieContact.ContactEmail)
				{
					// The current user already has a contact and the one on the cookie is the same
					// so we don't need to do anything else.
					return;
				}

				if (currentContact == null)
				{
					// The current user doesn't have a contact. If the one based on the cookie is anonymous though
					// we can use that and assign it to the user without creating a new one.
					if (cookieContact.ContactIsAnonymous)
					{
						currentContact = cookieContact;
					}
					else
					{
						// The contact retrieved based on the cookie value is for a different user.
						// We can't use that and that's the entire reason for having to write this class.
						currentContact = _contactCreator.Value.CreateAnonymousContact();
					}

					// We should have a contact we can use now that does not yet have a user assigned to it.
					// Sanity check this assertion just in case though.
					if (currentContact.Users.Count > 0)
						throw new KenticoContactException($"The current contact already has users assigned to it. This is not permitted. Id: {currentContact.ContactID}, GUID: {currentContact.ContactGUID}, Email: {currentContact.ContactEmail}");

					// This converts the anonymous contact into a real one for the user.
					_contactRelationAssigner.Value.Assign(userInfo, currentContact);
				}

				// Determine if we need to merge the current contact with the cookie one.
				if (currentContact != cookieContact && cookieContact.ContactIsAnonymous)
					_contactMergeService.Value.MergeContacts(cookieContact, currentContact);

				// Always ensure the correct contact is being used.
				// This has the effect of updating the contact id stored in the CurrentContact cookie
				// so all subsequent requests will be for the correct contact.
				_contactPersistentStorage.Value.SetPersistentContact(currentContact);
			}
			catch (Exception exc) when (_log.WriteError(exc, new { userName }, returnValue: true))
			{
				throw new KenticoContactException("There has been a problem merging the contact data for the specified user.", exc);
			}
		}

		public void ContingentMerge(IOwinContext owinContext, string currentSiteName, bool reset, Func<CookieOptions> cookieOptionsBuilder)
		{
			owinContext.Request.CallCancelled.ThrowIfCancellationRequested();
			Guard.ArgumentNotNull(owinContext, nameof(owinContext));
			Guard.ArgumentNotNullOrWhiteSpace(currentSiteName, nameof(currentSiteName));
			Guard.ArgumentNotNull(cookieOptionsBuilder, nameof(cookieOptionsBuilder));

			try
			{
				string currentCookieValue = null;
				List<string> lstCurrentlyMergedSites = null;

				if (!reset)
				{
					currentCookieValue = owinContext.Request.Cookies[_options.CrossSiteTrackingCookieName];

					if (!string.IsNullOrWhiteSpace(currentCookieValue))
					{
						try
						{
							lstCurrentlyMergedSites = UmbrellaStatics.DeserializeJson<List<string>>(currentCookieValue);
						}
						catch (Exception exc) when (_log.WriteWarning(exc, new { currentCookieValue }, $"The cookie value of the {_options.CrossSiteTrackingCookieName} could not be deserialized to a List<string> instance.", returnValue: true))
						{
							// If the cookie couldn't be deserialized it has probably been tampered with.
						}
					}
				}

				if (lstCurrentlyMergedSites == null)
					lstCurrentlyMergedSites = new List<string>();

				// At this point we should have either a new list to work with if we are resetting the cookie value
				// or an existing list from the cookie when not resetting. Either way, the next steps are the same.
				string currentSiteNameCleaned = currentSiteName.TrimToLowerInvariant();

                // Even though the cookie may have already been processed, we need to force processing in the event that there isn't a CurrentContact cookie
                // because it has been deleted manually or its value is invalid.
                if (!lstCurrentlyMergedSites.Contains(currentSiteNameCleaned) || !CurrentContactExists(owinContext))
                {
					// We haven't merged the contact for this site yet.
					Merge(owinContext.Request.User.Identity.Name);

					lstCurrentlyMergedSites.Add(currentSiteNameCleaned);

					// Update the cookie value on the outgoing response so that on the next request the merge doesn't happen again.
					// Cleanup the values in case they have been tampered with or mangled somehow.
					lstCurrentlyMergedSites = lstCurrentlyMergedSites.Select(x => x.TrimToLowerInvariant()).Distinct().ToList();

					string updatedCookieValue = UmbrellaStatics.SerializeJson(lstCurrentlyMergedSites);

					// We only need to update the cookie if its value has actually changed
					if (!string.Equals(currentCookieValue, updatedCookieValue, StringComparison.OrdinalIgnoreCase))
						owinContext.Response.Cookies.Append(_options.CrossSiteTrackingCookieName, updatedCookieValue, cookieOptionsBuilder());
				}
			}
			catch (Exception exc) when (_log.WriteError(exc, new { owinContext.Request.User.Identity.Name, reset }, returnValue: true))
			{
				throw new KenticoContactException("There has been a problem merging the contact data for the specified user for the specified OwinContext.", exc);
			}
		}

        private bool CurrentContactExists(IOwinContext owinContext)
        {
            // Only check the Kentico database if explicitly asked to.
            if (_options.CheckCurrentContactWithPersistentStorageDuringValidation)
                return _contactPersistentStorage.Value.GetPersistentContact() != null;

            // Just confirm the presence of the cookie by default.
            return !string.IsNullOrWhiteSpace(owinContext.Request.Cookies["CurrentContact"]);
        }
    }
}