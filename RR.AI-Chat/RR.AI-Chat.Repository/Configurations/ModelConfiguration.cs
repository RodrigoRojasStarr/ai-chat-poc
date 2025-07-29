using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RR.AI_Chat.Dto.Enums;
using RR.AI_Chat.Entity;

namespace RR.AI_Chat.Repository.Configurations
{
    public class ModelConfiguration : IEntityTypeConfiguration<Model>
    {
        public void Configure(EntityTypeBuilder<Model> builder)
        {
            var dateCreated = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            builder.HasData(
                new Model
                {
                    Id = new("157b91cf-1880-4977-9b7a-7f80f548df04"),
                    AIServiceId = AIServiceType.AzureOpenAI,
                    Name = "starr-gpt41-latest",
                    IsToolEnabled = true,
                    DateCreated = dateCreated
                }
            );
        }
    }
}
