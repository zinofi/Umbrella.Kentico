using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Umbrella.Kentico.Utilities.ContactManagement.Options
{
    /// <summary>
    /// Options for the <see cref="KenticoContactManager"/>.
    /// </summary>
    public class KenticoContactManagerOptions
    {
        /// <summary>
        /// Gets or sets the name of the cross site tracking cookie. Defaults to "CurrentContactXsTracker".
        /// </summary>
        public string CrossSiteTrackingCookieName { get; set; } = "CurrentContactXsTracker";

        /// <summary>
        /// Gets or sets a value indicating whether the value of the CurrentContact cookie should be checked to see if it exists during the initial validation process.
        /// By default, the only validation that will occur will be to check to ensure the cookie exists; not whether its value is actually valid. Validating the CurrentContact cookie
        /// value has a performance penalty as it requires the Kentico database to be hit and so this property is set to <see langword="false"/> by default.
        /// </summary>
        public bool CheckCurrentContactWithPersistentStorageDuringValidation { get; set; }
    }
}