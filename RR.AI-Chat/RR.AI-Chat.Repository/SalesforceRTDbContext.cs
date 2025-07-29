using Microsoft.EntityFrameworkCore;
using RR.AI_Chat.Entity.SalesforceRT;

namespace RR.AI_Chat.Repository
{
    public class SalesforceRTDbContext(DbContextOptions<SalesforceRTDbContext> options) : DbContext(options)
    {
        #region DbSets
        public DbSet<Contact> Contacts { get; set; }
        #endregion
    }
}
