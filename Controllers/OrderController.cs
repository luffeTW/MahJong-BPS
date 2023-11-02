using MahJongBPS.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;

namespace MahJongBPS.Controllers
{
    
    [Route("api/[controller]")]
    [ApiController]
    public class OrderController : ControllerBase
    {
        private readonly ILogger<OrderController> _logger;
        private readonly AppDbContext _context; //資料庫
        private readonly ISerialPortService _serialPortService;

        public OrderController(ILogger<OrderController> logger , AppDbContext context, ISerialPortService serialPortService)
        {
            _serialPortService = serialPortService;
            _logger = logger;
            _context = context;//資料庫
        }

        [HttpPost("Checkout")]
        public IActionResult Checkout([FromQuery] int tableNumber,[FromQuery]string tableName, [FromQuery] int hours, [FromQuery] int hourlyRate, [FromQuery] int totalAmount)
        {

            _logger.LogInformation($"寫入資料庫==>桌號:{tableNumber},購買時數:{hours},結帳金額:{totalAmount},小時費率:{hourlyRate}"); // 輸出金額到控制台
                                                                                                                   // 获取当前日期
            DateTime currentDate = DateTime.UtcNow.Date;

            // 查询数据库，获取当天已生成的订单数量
            int todayOrderCount = _context.Orders
                .Where(o => o.OrderDate.Date == currentDate)
                .Count();

            // 生成流水号
            Int64 orderNumber = Int64.Parse($"{currentDate:yyyyMMdd}{todayOrderCount + 1:D4}");
            _logger.LogInformation(orderNumber.ToString());
            var newOrder = new Order
            {
                OrderId = orderNumber,
                TableId = tableNumber,
                TotalHour = hours,
                TotalAmount = totalAmount,
                OrderDate = DateTime.UtcNow // 设置订单日期为当前的 Utc 时间
            };
            _context.Orders.Add(newOrder);
            try
            {
                _context.SaveChanges();
                _logger.LogInformation("成功對資料庫新增訂單紀錄");
                _serialPortService.recipt(orderNumber, tableName, tableNumber, hours, totalAmount, DateTime.Now);
                return Ok(new { message = "成功對資料庫新增訂單紀錄" });
            }
            catch (Exception ex)
            {
                _logger.LogInformation($"對資料庫新增訂單紀錄失敗:{ex}");
                return Content($"對資料庫新增訂單紀錄失敗:{ex}");
            }

        }

        //[HttpGet("GetTodayRevenue")]
        //public IActionResult GetTodayRevenue()
        //{
        //    DateTime date = DateTime.UtcNow.Date;
        //    int dailyRevenue = GetDailyRevenue(date);
        //    //_logger.LogInformation($"{dailyRevenue}");
        //    return Ok(new { date, dailyRevenue });
        //}

        [HttpGet("DailyRevenue")]
        public IActionResult GetDailyRevenue(DateTime date)
        {
            try
            {
                // 查询指定日期的所有订单
                var orders = _context.Orders
                    .Where(o => o.OrderDate.Date == date.Date)
                    .ToList();

                // 计算当日总營業額
                decimal dailyRevenue = orders.Sum(o => o.TotalAmount);

                return Ok(new { date, dailyRevenue });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error querying daily revenue: {ex}");
                return StatusCode(500, new { message = "Internal server error" });
            }
        }

        [HttpGet("MonthlyRevenue")]
        public IActionResult GetMonthlyRevenue(int year, int month)
        {
            try
            {
                // 查询指定年份和月份的所有订单
                var orders = _context.Orders
                    .Where(o => o.OrderDate.Year == year && o.OrderDate.Month == month)
                    .ToList();

                // 计算指定月份总營業額
                decimal monthlyRevenue = orders.Sum(o => o.TotalAmount);

                return Ok(new { year, month, monthlyRevenue });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error querying monthly revenue: {ex}");
                return StatusCode(500, new { message = "Internal server error" });
            }
        }
        [HttpGet("YearlyRevenue")]
        public IActionResult GetYearlyRevenue(int year)
        {
            try
            {
                var orders = _context.Orders
                   .Where(o => o.OrderDate.Year == year )
                   .ToList();

                decimal yearlyRevenue = orders.Sum(o => o.TotalAmount);

                return Ok(new { year, yearlyRevenue });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error querying monthly revenue: {ex}");
                return StatusCode(500, new { message = "Internal server error" });
            }
        }
        [HttpGet("OrdersByDateRange")]
        public IActionResult GetOrdersByDateRange(DateTime startDate, DateTime endDate)
        {
            try
            {
                // 查询指定日期范围内的所有订单
                var orders = _context.Orders
                    .Where(o => o.OrderDate >= startDate.Date && o.OrderDate <= endDate.Date)
                    .ToList();

                return Ok(orders);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error querying orders by date range: {ex}");
                return StatusCode(500, new { message = "Internal server error" });
            }
        }

        //新增查詢方法
    }
}
