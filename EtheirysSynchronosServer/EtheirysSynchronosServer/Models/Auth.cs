using System.ComponentModel.DataAnnotations;

namespace EtheirysSynchronosServer.Models
{
    public class Auth
    {
        [Key]
        [MaxLength(64)]
        public string HashedKey { get; set; }

        public string UserUID { get; set; }
        public User User { get; set; }
    }
}
