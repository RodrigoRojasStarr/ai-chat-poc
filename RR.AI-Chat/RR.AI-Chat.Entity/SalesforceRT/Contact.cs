using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RR.AI_Chat.Entity.SalesforceRT
{
    [Table("SF_CONTACT", Schema = "dbo")]
    public class Contact
    {
        [Key]
        [Column("ID")]
        [StringLength(18)]
        public string Id { get; set; } = null!;

        [Column("LASTNAME")]
        [StringLength(80)]
        public string? LastName { get; set; }

        [Column("FIRSTNAME")]
        [StringLength(40)]
        public string? FirstName { get; set; }

        [Column("PHONE")]
        [StringLength(40)]
        public string? Phone { get; set; }

        [Column("EMAIL")]
        [StringLength(80)]
        public string? Email { get; set; }

        [Column("CREATEDDATE")]
        public DateTime? CreatedDate { get; set; }

        [Column("LASTMODIFIEDBYID")]
        [StringLength(18)]
        public string? LastModifiedById { get; set; }

        [Column("SYSTEMMODSTAMP")]
        public DateTime? SystemModStamp { get; set; }

        [Column("CONTACT_INACTIVE__C")]
        public int? ContactInactive { get; set; }

        [Column("PRODUCER__C")]
        [StringLength(18)]
        public string? ProducerId { get; set; }

        [Column("RTCreatedDate")]
        public DateTime? RTCreatedDate { get; set; }

        [Column("RTModifiedDate")]
        public DateTime? RTModifiedDate { get; set; }

        [NotMapped]
        public bool IsInactive => ContactInactive == 1;

        [NotMapped]
        public DateTime? DateDeactivated => IsInactive ? SystemModStamp : null;
    }
}
