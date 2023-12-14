using Microsoft.AspNetCore.Mvc;

namespace MahJongBPS.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RoomController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILogger<OrderController> _logger;

        public RoomController(ILogger<OrderController> logger, AppDbContext context)
        {
            _logger = logger;
            _context = context;
        }                           
        [HttpGet("GetRoom")]
        public IActionResult GetRoom(int CategoryId)
        {
            var Rooms = _context.Room
                .Where(ts=> ts.RoomCategoryId == CategoryId)
                .ToList();
            foreach (var room in Rooms)
            {
                Console.WriteLine($"[{room.RoomId}]:{room.RoomName} {room.RoomCategoryId}");
            }
            //return Ok(Rooms);

            // 將符合條件的選項按照價格排序

            // 將結果轉換為 JSON 格式並返回到前端
            return Ok(Rooms);
        }
    }
}
