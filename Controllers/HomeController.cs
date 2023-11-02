using MahJongBPS.Models;
using MahJongBPS.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Serilog;
using System.IO.Ports;
using System.Text;


namespace MahJongBPS.Controllers
{

    public class HomeController : Controller
    {
        
        private readonly ILogger<HomeController> _logger;
        
        private readonly ISerialPortService _serialPortService;

        private readonly IHubContext<NotificationHub> _hubContext;

        public HomeController(ILogger<HomeController> logger, ISerialPortService serialPortService, IHubContext<NotificationHub> hubContext)
        {
            _serialPortService = serialPortService;
            _logger = logger;
            _hubContext = hubContext;
        }

       

        public string GenerateFullCommand(string shortCommand, int value, int amount)
        {
            string commandOnly = shortCommand;
            // 檢查是否符合例外轉換規則

            if (commandOnly == "10" || commandOnly == "01" || commandOnly == "11")
            {
                //int totalValue = int.Parse(textbox1.Text) * 100;
                int totalValue = value * 100;
                // 將 totalValue 轉換成十六進位字串
                // string totalValueHex = totalValue.ToString("X");
                string totalValueHex = totalValue.ToString("X");
                // 將 totalValueHex 轉換成 result 格式
                string result = totalValueHex.PadLeft(8, '0');
                // 將 result 每 2 個字符為一組，並將順序反轉
                var chunks = Enumerable.Range(0, result.Length / 2)
                                       .Select(i => result.Substring(i * 2, 2))
                                       .Reverse();
                // 將 chunks 合併成最終的字串
                result = string.Join(" ", chunks);

                byte instructionCode = 0x00; // 預設指令碼為 0x00，可以自行決定預設值
                if (commandOnly == "10")
                {
                    instructionCode = 0x10;
                }
                else if (commandOnly == "01")
                {
                    instructionCode = 0x01;
                }
                else if (commandOnly == "11")
                {
                    instructionCode = 0x11;
                }
                // 資料長度碼為 0x06
                byte dataLengthCode = 0x06;
                // 參數碼為 textbox1 的內容乘以 100 轉換成整數
                //int parameterValue = int.Parse(textbox1.Text) * 100;
                int parameterValue = value * 100;
                // 將參數碼轉換成 16 進位字串
                string parameterHex = parameterValue.ToString("X");
                // 確保 parameterHex 長度為偶數
                if (parameterHex.Length % 2 != 0)
                {
                    parameterHex = "0" + parameterHex;
                }
                // 將 parameterHex 轉換成 byte 陣列
                byte[] parameterBytes = Enumerable.Range(0, parameterHex.Length / 2)
                                                  .Select(i => Convert.ToByte(parameterHex.Substring(i * 2, 2), 16))
                                                  .ToArray();
                // 計算 sum
                int sum = instructionCode + dataLengthCode;
                foreach (byte b in parameterBytes)
                {
                    sum += b;
                    // 如果 sum 超過 0xFF，則只取後兩位數
                    sum &= 0xFF;
                }
                // 計算 CRC
                byte crc = (byte)((0xFF - sum) + 0x01);
                // 將 sum 和 CRC 轉換成十六進位字串
                string sumHex = sum.ToString("X2");
                string crcHex = crc.ToString("X2");
                // 使用 StringBuilder 來構建完整指令
                StringBuilder fullCommandBuilder = new StringBuilder();
                fullCommandBuilder.Append("40 06 00 00 ");
                fullCommandBuilder.Append(commandOnly);
                fullCommandBuilder.Append(" ");
                fullCommandBuilder.Append(result);
                fullCommandBuilder.Append(" ");
                fullCommandBuilder.Append(crcHex);
                fullCommandBuilder.Append(" 7F 7E");
                // fullCommandBuilder.Append(sumHex);
                string fullCommand = fullCommandBuilder.ToString();
                return fullCommand;
            }
            else if (commandOnly == "07" || commandOnly == "08")
            {
                byte instructionCode = 0x00; // 預設指令碼為 0x00，可以自行決定預設值
                if (commandOnly == "07")
                {
                    instructionCode = 0x07;
                }
                else if (commandOnly == "08")
                {
                    instructionCode = 0x08;
                }
                // 資料長度碼為 0x0A
                byte dataLengthCode = 0x0A;
                //int totalValue2 = int.Parse(textBox2.Text);
                int totalValue2 = amount;
                // 將 totalValue 轉換成十六進位字串
                string totalValueHex2 = totalValue2.ToString("X");
                // 將 totalValueHex 轉換成 result 格式
                string result2 = totalValueHex2.PadLeft(8, '0');
                // 將 result 每 2 個字符為一組，並將順序反轉
                var chunk2 = Enumerable.Range(0, result2.Length / 2)
                                       .Select(i => result2.Substring(i * 2, 2))
                                       .Reverse();
                // 將 chunks 合併成最終的字串
                result2 = string.Join(" ", chunk2);

                //int totalValue1 = int.Parse(textbox1.Text) * 100;
                int totalValue1 = value * 100;
                // 將 totalValue 轉換成十六進位字串
                string totalValueHex1 = totalValue1.ToString("X");
                // 將 totalValueHex 轉換成 result 格式
                string result1 = totalValueHex1.PadLeft(8, '0');
                // 將 result 每 2 個字符為一組，並將順序反轉
                var chunk1 = Enumerable.Range(0, result1.Length / 2)
                                       .Select(i => result1.Substring(i * 2, 2))
                                       .Reverse();
                // 將 chunks 合併成最終的字串
                result1 = string.Join(" ", chunk1);
                // 參數碼為 textbox 的內容轉換成整數
                //int parameterValue2 = int.Parse(textBox2.Text);
                int parameterValue2 = amount;

                // 將參數碼轉換成 16 進位字串
                string parameterHex2 = parameterValue2.ToString("X");
                // 確保 parameterHex 長度為偶數
                if (parameterHex2.Length % 2 != 0)
                {
                    parameterHex2 = "0" + parameterHex2;
                }
                // 將 parameterHex 轉換成 byte 陣列
                byte[] parameterBytes2 = Enumerable.Range(0, parameterHex2.Length / 2)
                                                  .Select(i => Convert.ToByte(parameterHex2.Substring(i * 2, 2), 16))
                                                  .ToArray();
                // 計算 sum
                int sum = instructionCode + dataLengthCode;
                foreach (byte b in parameterBytes2)
                {
                    sum += b;
                    // 如果 sum 超過 0xFF，則只取後兩位數
                    sum &= 0xFF;
                }
                // 參數碼為 textbox 的內容乘以 100 轉換成整數
                //int parameterValue1 = int.Parse(textbox1.Text) * 100;
                int parameterValue1 = value * 100;
                // 將參數碼轉換成 16 進位字串
                string parameterHex1 = parameterValue1.ToString("X");
                // 確保 parameterHex 長度為偶數
                if (parameterHex1.Length % 2 != 0)
                {
                    parameterHex1 = "0" + parameterHex1;
                }
                // 將 parameterHex 轉換成 byte 陣列
                byte[] parameterBytes1 = Enumerable.Range(0, parameterHex1.Length / 2)
                                                  .Select(i => Convert.ToByte(parameterHex1.Substring(i * 2, 2), 16))
                                                  .ToArray();
                // 計算 sum
                foreach (byte b in parameterBytes1)
                {
                    sum += b;
                    // 如果 sum 超過 0xFF，則只取後兩位數
                    sum &= 0xFF;
                }
                // 計算 CRC
                byte crc = (byte)((0xFF - sum) + 0x01);
                // 將 sum 和 CRC 轉換成十六進位字串
                string sumHex = sum.ToString("X2");
                string crcHex = crc.ToString("X2");
                // 使用 StringBuilder 來構建完整指令
                StringBuilder fullCommandBuilder = new StringBuilder();
                fullCommandBuilder.Append("40 0A 00 00 ");
                fullCommandBuilder.Append(commandOnly);
                fullCommandBuilder.Append(" ");
                fullCommandBuilder.Append(result1);
                fullCommandBuilder.Append(" ");
                fullCommandBuilder.Append(result2);
                fullCommandBuilder.Append(" ");
                fullCommandBuilder.Append(crcHex);
                fullCommandBuilder.Append(" 7F 7E");
                // fullCommandBuilder.Append(sumHex);

                string fullCommand = fullCommandBuilder.ToString();
                return fullCommand;
            }
            else if (commandOnly == "13")
            {
                string fullCommand = "40 03 00 00 13 01 E9 7F 7E";
                return fullCommand;
            }
            else
            {
                int hexValue = Convert.ToInt32(commandOnly, 16);
                int crc = 0xFE - hexValue;
                string fullCommand = string.Format("40 02 00 00 {0:X2} {1:X2} 7F 7E", hexValue, crc);
                return fullCommand;
            }
        }

        [HttpPost("GenerateFullCommand")]
        public IActionResult GenerateFullCommand([FromBody] GenerateFullCommandRequest request)
        {
            string fullCommand = GenerateFullCommand(request.ShortCommand, request.Value, request.Amount);
            _logger.LogInformation(fullCommand);

            // 发送生成的指令到串口
            _serialPortService.PS100Write(fullCommand);

            return Json(new { fullCommand = fullCommand }); // 回傳包含 fullCommand 屬性的 JSON 物件
        }

        [HttpPost("Checkout_1")]
        public IActionResult Checkout_1()
        {
            _serialPortService.Checkout_1();
            return Json(new { message = "操作成功" });
        }
        [HttpPost("Checkout_2")]
        public IActionResult Checkout_2([FromQuery] int CheckoutAmount)
        {
            _serialPortService.Checkout_2(CheckoutAmount);
            return Json(new { message = "操作成功" });

        }
        [HttpPost("Checkout_3")]
        public IActionResult Checkout_3()
        {
            _serialPortService.Checkout_3();
            return Json(new { message = "操作成功" });
        }
        [HttpPost("WriteToMH")]
        public IActionResult WriteToMH(string data)
        {
            _serialPortService.MHWrite(data);
            return Json(new { message = "操作成功" });
        }
        [HttpPost("MHDispense")]
        public IActionResult MHDispense (int data)
        {
            _serialPortService.MHdispense(data);
            return Json(new { message = "操作成功" });
        }
        [HttpPost("WriteToXC100")]
        public IActionResult WriteToXC100([FromBody] RequestXC100 request)
        {
            string command = request.command;
            int amount = request.amount;
            _logger.LogInformation($"Command:{command} Data:{amount} ");
            if (command == "B" && amount == 0)
            {
                _logger.LogInformation("出鈔金額不得為零");
                return Json(new { message = "出鈔金額不得為零" });
            }
            _serialPortService.XC100Write(command, amount);
            return Json(new { message = "操作成功" });
        }
        [HttpPost("XC100StockUpdate")]
        public IActionResult XC100StockUpdate(int Amount)
        {
            int method = 1; //0:增加放入的鈔票數量   1:更新目前的鈔票數量
            _serialPortService.XC100StockUpdate(Amount, 0);
            return Json(new { message = "操作成功" });
        }

        [HttpPost("XC100StockScan")]
        public IActionResult XC100StockScan()
        {
            int str = _serialPortService.XC100StockScan();
            return Json(new { data = str });
        }
        [HttpPost("MHstockUpdate")]
        public IActionResult MHstockUpdate(int amount)
        {
            int method = 0; //0:增加放入的硬幣數量   1:更新目前的硬幣數量
            _serialPortService.MHstockUpdate( amount,  method);
            return Json(new { message = "操作成功" });
        }

        public class RequestXC100
        {
            public string command { get; set; }
            public int amount { get; set; }
        }
        public class GenerateFullCommandRequest
        {
            public string ShortCommand { get; set; }
            public int Value { get; set; }
            public int Amount { get; set; }
        }

        public IActionResult Index()
        {
            _logger.LogInformation("訪問首頁");
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        public IActionResult AdminPanel()
        {
            _logger.LogInformation("訪問管理員後台");
            return View();
        }
        // 其他方法...
    }
}