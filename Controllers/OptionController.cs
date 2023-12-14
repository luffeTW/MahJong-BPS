using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MahJongBPS.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OptionController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILogger<OrderController> _logger;

        public OptionController(ILogger<OrderController> logger ,AppDbContext context)
        {
            _logger = logger;
            _context = context;
        }

        [HttpGet("GetTimeSlot")]
        public IActionResult GetTimeSlot(TimeSpan timeSpan, DayOfWeek dayOfWeek)
        {
            var matchingTimeSlots = _context.TimeSlot
                .Where(ts =>
                    ts.DayOfWeek.Contains((int)dayOfWeek) && // 檢查是否在該星期幾的時段中
                    (
                        (ts.StartTime < ts.EndTime && ts.StartTime <= timeSpan && ts.EndTime >= timeSpan) ||
                        (ts.StartTime > ts.EndTime && (ts.StartTime <= timeSpan || ts.EndTime >= timeSpan))
                    )
                )
                .OrderByDescending(ts => ts.Priority) // 依照優先度降序排列
                .ToList(); // 取得所有符合條件的時段

            // 取得最高優先度
            var highestPriority = matchingTimeSlots.FirstOrDefault()?.Priority;

            // 選取所有符合條件且優先度相同的時段
            var highestPriorityTimeSlots = matchingTimeSlots
                .Where(ts => ts.Priority == highestPriority)
                .ToList();
            Console.WriteLine($"查詢符合的時段 星期:{dayOfWeek} 時間:{timeSpan}");
            foreach (var timeSlot in highestPriorityTimeSlots)
            {
                string daysOfWeek = string.Join(",", timeSlot.DayOfWeek);
                Console.WriteLine($"符合時段：{timeSlot.SlotId}.){timeSlot.SlotName}, {timeSlot.StartTime} 到 {timeSlot.EndTime}, 優先度:{timeSlot.Priority}, 每周:" + "{"+daysOfWeek+"}");
            }
            return Ok(new { message = "操作成功" });
        }
        [HttpGet("GetOption")]
        public IActionResult GetOption(int TableCategory)
        {
            DateTime now = DateTime.Now; // 取得目前的日期和時間
            TimeSpan currentTime = now.TimeOfDay; // 取得目前的時間
            DayOfWeek currentDayOfWeek = now.DayOfWeek; // 取得目前的星期幾
            Console.WriteLine($"{currentTime}/{currentDayOfWeek}");
            var matchingTimeSlots = _context.TimeSlot
                .Where(ts =>
                    ts.DayOfWeek.Contains((int)currentDayOfWeek) && // 檢查是否在該星期幾的時段中
                    (
                        (ts.StartTime < ts.EndTime && ts.StartTime <= currentTime && ts.EndTime >= currentTime) ||
                        (ts.StartTime > ts.EndTime && (ts.StartTime <= currentTime || ts.EndTime >= currentTime))
                    )
                )
                .OrderByDescending(ts => ts.Priority) // 依照優先度降序排列
                .ToList(); // 取得所有符合條件的時段

            // 取得最高優先度
            var highestPriority = matchingTimeSlots.FirstOrDefault()?.Priority;

            // 選取所有符合條件且優先度相同的時段
            var highestPriorityTimeSlots = matchingTimeSlots
                .Where(ts => ts.Priority == highestPriority)
                .ToList();

            foreach (var timeSlot in highestPriorityTimeSlots)
            {
                string daysOfWeek = string.Join(",", timeSlot.DayOfWeek);
                //Console.WriteLine($"符合時段：{timeSlot.SlotId}.){timeSlot.SlotName}, {timeSlot.StartTime} 到 {timeSlot.EndTime}, 優先度:{timeSlot.Priority}, 每周:" + "{" + daysOfWeek + "}");
            }
            // 設定條件 matchingTimeSlots（符合條件的時段ID列表）和 userSelectedRoomCategoryId（使用者選擇的房間ID）
            var userSelectedRoomCategoryId = TableCategory;
            // 查詢符合條件的選項
            var matchingOptions = _context.Option
                .Where(opt =>
                    highestPriorityTimeSlots.Select(ts => ts.SlotId).Contains(opt.TimeSlotId) && // 檢查時段ID是否在列表中
                    opt.RoomCategoryId == userSelectedRoomCategoryId // 檢查房間ID是否符合使用者選擇的
                )
                .ToList();

            // 輸出符合條件的選項
            foreach (var option in matchingOptions)
            {
                Console.WriteLine($"符合選項：{option.OptionId}.){option.Name}, 價格: {option.Price}, 時間: {option.Minutes} 分鐘, 種類ID:{option.RoomCategoryId}, 時段ID:{option.TimeSlotId}");            
            }

            // 將符合條件的選項按照價格排序
            var sortedOptions = matchingOptions.OrderBy(opt => opt.Price).ToList();

            // 將結果轉換為 JSON 格式並返回到前端
            return Ok(sortedOptions.Select(opt => new { Name = opt.Name, Price = opt.Price, Minutes = opt.Minutes }));
        }
    }
    
}
