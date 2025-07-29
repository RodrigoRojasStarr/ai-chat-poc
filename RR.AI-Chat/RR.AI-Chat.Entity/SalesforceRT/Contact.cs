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

        [Column("PRODUCER__C")]
        [StringLength(18)]
        public string? ProducerId { get; set; } = null!;

        [Column("FIRSTNAME")]
        [StringLength(40)]
        public string? FirstName { get; set; }

        [Column("LASTNAME")]
        [StringLength(80)]
        public string? LastName { get; set; }

        [Column("NAME")]
        [StringLength(121)]
        public string? Name { get; set; }

        [Column("EMAIL")]
        [StringLength(80)]
        public string? Email { get; set; }

        [Column("ISEMAILBOUNCED")]
        public int? IsEmailedBounced { get; set; }

        [Column("EMAILBOUNCEDREASON")]
        [StringLength(255)]
        public string? EmailBouncedReason { get; set; }

        [Column("EMAILBOUNCEDDATE")]
        public DateTime? EmailBouncedDate { get; set; }

        [Column("HASOPTEDOUTOFEMAIL")]
        public int? IsOptOutOfEmail { get; set; }

        [Column("PHONE")]
        [StringLength(40)]
        public string? Phone { get; set; }

        [Column("MOBILEPHONE")]
        [StringLength(40)]
        public string? MobilePhone { get; set; }

        [Column("DONOTCALL")]
        public int? IsDontCall { get; set; }

        [Column("Role__C")]
        [StringLength(255)]
        public string? Role { get; set; }

        [Column("COMPANY_NAME__C")]
        [StringLength(1300)]
        public string? CompanyName { get; set; }

        [Column("STARR_UNIQUE_ID__C")]
        [StringLength(30)]
        public string? StarrUniqueId { get; set; }

        [Column("CREATEDDATE")]
        public DateTime? DateCreated { get; set; }

        [Column("SYSTEMMODSTAMP")]
        public DateTime? DateModified { get; set; }

        [Column("CONTACT_INACTIVE__C")]
        public int? IsInactive { get; set; }

        [StringLength(18)]
        [Column("RECORDTYPEID")]
        public string? RecordTypeId { get; set; }

        [NotMapped]
        public DateTime? DateDeactivated
        {
            get
            {
                return IsInactive == 1 ? DateModified : null;
            }
        }
    }
}
