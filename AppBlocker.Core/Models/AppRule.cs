using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AppBlocker.Core.Models
{
    [Table("AppRulesTable")]
    public class AppRule
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(260)]
        public string ProcessName { get; set; } = string.Empty;

        [MaxLength(1024)]
        public string? ExePath { get; set; }

        public bool IsActive { get; set; } = true;

        [Required]
        [MaxLength(50)]
        public string ScheduleType { get; set; } = "Always";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
