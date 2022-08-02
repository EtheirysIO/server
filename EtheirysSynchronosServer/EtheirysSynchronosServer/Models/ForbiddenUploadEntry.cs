using System.ComponentModel.DataAnnotations;

namespace EtheirysSynchronosServer.Models
{
    public class ForbiddenUploadEntry
    {
        [Key]
        [MaxLength(40)]
        public string Hash { get; set; }
        public string ForbiddenBy { get; set; }
        [Timestamp]
        public byte[] Timestamp { get; set; }
    }
}
