using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace CMPS4110_NorthOaksProj.Models.Users
{
    public class User : IdentityUser<int>
    {

        [Required]
        public string FirstName { get; set; }

        [Required]
        public string LastName { get; set; }

    }

}
