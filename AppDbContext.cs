using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace MahJongBPS
{
    public class Order
    {
        public Int64 OrderId { get; set; }
        public decimal TableId { get; set; }
        public decimal TotalMinutes { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime OrderDate { get; set; }
    }
    public class Option
    {
        [Key]
        public int OptionId { get; set; }
        public string Name { get; set; }
        public int Price { get; set; }
        public int Minutes { get; set; }
        public int RoomCategoryId { get; set; }
        public int TimeSlotId { get; set; }

        public virtual RoomCategory RoomCategory { get; set; }
        public virtual TimeSlot TimeSlot { get; set; }
    }
    public class RoomCategory
    {
        [Key]
        public int CategoryId { get; set; }
        public string CategoryName { get; set; }

        // 定義 Option 表格的外部鍵
        public virtual ICollection<Option> Options { get; set; }
        public virtual ICollection<Room> Rooms { get; set; }
    }
    public class Room
    {
        [Key]
        public int RoomId { get; set;}
        public string RoomName { get; set; }
        public int RoomCategoryId { get; set; }

        public virtual RoomCategory RoomCategory { get; set; }
    }
    public class TimeSlot
    {
        [Key]
        public int SlotId { get; set; }
        public string SlotName { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public int Priority { get; set; }
        public int[] DayOfWeek { get; set; }

        // 定義 Option 表格的外部鍵
        public virtual ICollection<Option> Options { get; set; }
    }
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Order> Orders { get; set; }
        public DbSet<Option> Option { get; set; }
        public DbSet<Room> Room { get; set; }
        public DbSet<RoomCategory> RoomCategory { get; set; }
        public DbSet<TimeSlot> TimeSlot { get; set; }

    }

}
