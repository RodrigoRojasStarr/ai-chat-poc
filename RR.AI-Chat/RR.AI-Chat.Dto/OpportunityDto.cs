namespace RR.AI_Chat.Dto
{
    public class OpportunityDto
    {
        public string ID { get; set; } = null!;

        public string? NAME { get; set; }

        public decimal? AMOUNT { get; set; }

        public DateTime? CREATEDDATE { get; set; }

        public DateTime? LASTMODIFIEDDATE { get; set; } 

        public string? ACCOUNTID { get; set; }

        public string? TYPE { get; set; }

        public string? ICE_STATUS__C { get; set; }

        public string? POLICY_NUMBER_CURRENT__C { get; set; }

        public string? STARR_UNIQUE_ID__C { get; set; }

        public string? NEW_PRODUCER__C { get; set; }

        public string? STAGENAME { get; set; }

        public string? ISSUING_OFFICE__C { get; set; }

        public string? PRODUCER_CONTACT_EMAIL__C { get; set; }

        public string? LINE_OF_BUSINESS__C { get; set; }

        public string? BUSINESS_UNIT__C { get; set; }

        public DateTime? EFFECTIVE_DATE__C { get; set; }

        public DateTime? EXPIRATION_DATE__C { get; set; }
    }
}
