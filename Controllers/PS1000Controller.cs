using MahJongBPS.Services;
using Microsoft.AspNetCore.Mvc;

namespace MahJongBPS.Controllers
{
    public class PS1000Controller : Controller
    {
        private readonly ISerialPortService _serialPortService;
        private readonly ILogger<PS1000Controller> _logger;
        public PS1000Controller(ISerialPortService serialPortService)
        {
            _serialPortService = serialPortService;
            Console.WriteLine("Service here");
        }
        public IActionResult Index()
        {
            //_logger.LogInformation("訪問首頁");
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

    }
}
