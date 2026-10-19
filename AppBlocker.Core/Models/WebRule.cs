using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AppBlocker.Core.Models
{
    [Table("WebRulesTable")]
    public class WebRule
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(512)]
        public string UrlOrKeyword { get; set; } = string.Empty;

        [MaxLength(1024)]
        public string? RedirectUrl { get; set; }

        public bool IsActive { get; set; } = true;

        [Required]
        [MaxLength(50)]
        public string ScheduleType { get; set; } = "Always";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
