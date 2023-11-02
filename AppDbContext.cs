using Microsoft.EntityFrameworkCore;
namespace MahJongBPS
{
    public class Order
    {
        public Int64 OrderId { get; set; }
        public decimal TableId { get; set; }
        public decimal TotalHour { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime OrderDate { get; set; }
    }

    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Order> Orders { get; set; }
    }

}
