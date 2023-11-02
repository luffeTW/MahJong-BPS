
using Microsoft.AspNetCore.Mvc;
using System.IO.Ports;
using System.Timers;
using Timer = System.Timers.Timer;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Text.Json;
using MahJongBPS.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;

namespace MahJongBPS.Controllers
{
    
    public class TimerData
    {
        public Timer Timer { get; set; }
        public DateTime StartTime { get; set; }

        public TimerData(System.Timers.Timer timer, DateTime startTime)
        {
            Timer = timer;
            StartTime = startTime;
        }
    }
   
    [Route("api/[controller]")]
    [ApiController]
    public class TableController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly AppSettings _appSettings;
        private readonly ILogger<TableController> _logger;     
        public static Dictionary<int, TimerData> tableTimers = new Dictionary<int, TimerData>();
        private static bool isInitialized = false;
        //public interface ITableController
        //{
        //    double Endtime(int tableNumber);
        //}

        public TableController(ILogger<TableController> logger, IConfiguration configuration, IOptions<AppSettings> appSettings)
        {
            _logger = logger;
            _configuration = configuration;
            _appSettings = appSettings.Value;
            if (!isInitialized)
            {
                InitializeTableTimers();
                isInitialized = true;
            }

        }
        private void InitializeTableTimers()
        {
            //_logger.LogInformation("載入桌號剩餘時間");
            isInitialized = true;
            // 获取配置文件中的 TableTimers 部分
            try
            {
                var tableTimersConfig = _configuration.GetSection("TableTimers").Get<Dictionary<int, int>>();
                foreach (var kvp in tableTimersConfig)
                {

                        int tableNumber = kvp.Key;
                        int remainingTime = kvp.Value;

                        Timer newTimer = new Timer();
                        newTimer.Interval = remainingTime * 1000;
                        newTimer.AutoReset = false; 
                        newTimer.Elapsed += (sender, e) => OnTimerElapsed(sender, e, tableNumber);
                        newTimer.Enabled = true;
                        tableTimers[tableNumber] = new TimerData(newTimer, DateTime.Now);
                        //_logger.LogInformation($"復原 [{remainingTime}秒] 至 [{tableNumber}號桌] ");

                }


            }
            catch (Exception ex)
            {
                _logger.LogInformation($"{isInitialized}載入桌號剩餘時間失敗 erro:{ex}");

            }



        }
        // 数据结构用于跟踪桌号的计时器
        [HttpPost("CheckOutTable")]
        public IActionResult CheckOutTable([FromQuery] int tableNumber, [FromQuery] int hours)
        {
            _logger.LogInformation($"CheckOutTable called with tableNumber: {tableNumber}, hours: {hours}");

            if (tableTimers.TryGetValue(tableNumber, out TimerData timerData))
            {
                // 获取原计时器的剩余时间
                double remainingMilliseconds = timerData.Timer.Interval - (DateTime.Now - timerData.StartTime).TotalMilliseconds;

                // 取消旧计时器
                timerData.Timer.Stop();
                timerData.Timer.Close();

                // 计算新的总时间（包括原剩余时间和加购时间）
                double newMilliseconds = remainingMilliseconds + (hours * 60 * 60 * 1000); // 将小时转换为毫秒
                Timer newTimer = new Timer();
                newTimer.Interval = newMilliseconds;
                newTimer.AutoReset = false; // 设置为 false，以便计时器在触发后不再重复触发
                newTimer.Elapsed += (sender, e) => OnTimerElapsed(sender, e, tableNumber); // 设置触发事件
                newTimer.Enabled = true; // Start the timer
                tableTimers[tableNumber] = new TimerData(newTimer, DateTime.Now);

                _logger.LogInformation($"加購 [{hours}小時] 至 [{tableNumber}號桌]");
            }
            else
            {
                // 创建新计时器并添加到字典
                double millisecondsToClose = hours * 60 * 60 * 1000; // 将小时转换为毫秒
                Timer timer = new Timer();
                timer.Interval= millisecondsToClose;
                timer.AutoReset = false; // 设置为 false，以便计时器在触发后不再重复触发
                timer.Elapsed += (sender, e) => OnTimerElapsed(sender, e, tableNumber); // 设置触发事件
                timer.Enabled = true; // Start the timer
                tableTimers.TryAdd(tableNumber, new TimerData(timer, DateTime.Now));

                // 发送开启指令
                byte[] openCommandBytes = ConvertTableToOpenCommand(tableNumber);
                PerformSerialPortAction(serialPort =>
                {
                    serialPort.Write(openCommandBytes, 0, openCommandBytes.Length);
                    _logger.LogInformation($"發送開啟[{tableNumber}號桌]");
                });

                _logger.LogInformation($"開啟 [{hours}小時] 至 [{tableNumber}號桌]");
            }

            //_logger.LogInformation($"Final tableTimers.Count: {tableTimers.Count}");

            return Json(new { message = "操作成功" });
        }

        // 定时器回调方法
        private void OnTimerElapsed(object sender, ElapsedEventArgs e, int tableNumber)
        {
            // 这里执行关闭桌号的操作
            PerformSerialPortAction(serialPort =>
            {
                byte[] closeCommandBytes = ConvertTableToCloseCommand(tableNumber);
                serialPort.Write(closeCommandBytes, 0, closeCommandBytes.Length);
                _logger.LogInformation($"關閉 [{tableNumber}號桌]");
            });

            // 在关闭操作完成后，从字典中移除计时器
            if (tableTimers.ContainsKey(tableNumber))
            {
                TimerData timerData = tableTimers[tableNumber];
                timerData.Timer.Close();
                tableTimers.Remove(tableNumber);
                _logger.LogInformation("timmer successfully removed");
            }
        }


        //public double Endtime(int tableNumber)
        //{
        //    tableTimers.TryGetValue(tableNumber, out TimerData timerData);
        //    double remainingTime = timerData.Timer.Interval - (DateTime.Now - timerData.StartTime).TotalMilliseconds;
        //    //Console.WriteLine(DateTime.Now.AddMilliseconds(remainingTime));
        //    //return DateTime.Now.AddMilliseconds(remainingTime);
        //    return remainingTime;
        //}
        public DateTime Endtime(int tableNumber)
        {
            tableTimers.TryGetValue(tableNumber, out TimerData timerData);
            double remainingTime = timerData.Timer.Interval - (DateTime.Now - timerData.StartTime).TotalMilliseconds;
            Console.WriteLine(DateTime.Now.AddMilliseconds(remainingTime));
            return DateTime.Now.AddMilliseconds(remainingTime);
        }


        [HttpPost("GetRemainingTime")]
        public IActionResult GetRemainingTime(int tableNumber)
        {
            try
            {
                tableTimers.TryGetValue(tableNumber, out TimerData timerData);
                
                    // 获取原计时器的剩余时间
                    double remainingTime = timerData.Timer.Interval - (DateTime.Now - timerData.StartTime).TotalMilliseconds;
                    
                

                _logger.LogInformation($"查詢桌號:{tableNumber} 剩餘時間:{remainingTime}秒");
                return Json(new { tableNumber, remainingTime });
            }
            catch
            {
                
                return Json(new { tableNumber, remainingTime = TimeSpan.Zero });
            }
        }
        
        public Dictionary<int, double> GetAllTableTimers()
        {
            var allTableTimers = new Dictionary<int, double>();

            foreach (var kvp in tableTimers)
            {
                int tableNumber = kvp.Key;
                TimerData timerData = kvp.Value;

                // 获取原计时器的剩余时间
                double remainingTime = timerData.Timer.Interval - (DateTime.Now - timerData.StartTime).TotalMilliseconds;

                allTableTimers[tableNumber] = remainingTime;
            }

            return allTableTimers;
        }


        



        [HttpPost("TableCommand")]
        public IActionResult TableCommand([FromQuery] int tableNumber, [FromQuery] int command)
        {
            if (command == 0)
            {
                PerformCloseAction(tableNumber);
                return Json(new { message = "操作成功" });
            }
            else if(command == 1)
            {
                PerformOpenAction(tableNumber);
                return Json(new { message = "操作成功" });
            }
            else
            {
                return Json(new {message =  "未知要求指令:" + command });
            }
            
        }



        // 执行关闭操作（发送关闭指令等）
        private void PerformCloseAction(int tableNumber)
        {
            byte[] closeCommandBytes = ConvertTableToCloseCommand(tableNumber);
            try
            {
                PerformSerialPortAction(serialPort =>
                {
                    // 发送开启指令
                    serialPort.Write(closeCommandBytes, 0, closeCommandBytes.Length);
                    _logger.LogInformation($"關閉 [{tableNumber}號桌]");
                });
            }catch (Exception ex)
            {
                _logger.LogInformation($"傳送 關閉 [{tableNumber}號桌] 失敗:{ex}");
            }
            

           
        }
        private void PerformOpenAction(int tableNumber)
        {
            byte[] closeCommandBytes = ConvertTableToOpenCommand(tableNumber);
            try
            {
                PerformSerialPortAction(serialPort =>
                {
                    // 发送开启指令
                    serialPort.Write(closeCommandBytes, 0, closeCommandBytes.Length);
                    _logger.LogInformation($"開啟 [{tableNumber}號桌]");
                });
            }
            catch (Exception ex)
            {
                _logger.LogInformation($"傳送 關閉 [{tableNumber}號桌] 失敗:{ex}");
            }



        }

        private void PerformSerialPortAction(Action<SerialPort> action)
        {
            string ModBusPort = _appSettings.ModBusPort;

            using (var serialPort = new SerialPort(ModBusPort, 9600))
            {
                if (serialPort.IsOpen==false)
                {
                    serialPort.Open();
                }
                    try
                {
                    
                    action(serialPort); // 执行操作
                }
                catch (Exception ex)
                {
                    _logger.LogInformation($"串口連接失敗：{ex.Message}");
                    // 处理连接失败情况
                }
            }
        }
        private byte[] ConvertTableToOpenCommand(int tableNumber)
        {
            
            //_logger.LogInformation("API function ConvertTableToOpenCommand deviceAddress{"+deviceAddress+"} tableNumber{"+tableNumber+"}");
            return GenerateRelayCommand(tableNumber, isTurnOn: true);
        }

        private byte[] ConvertTableToCloseCommand(int tableNumber)
        {          
            //_logger.LogInformation("API function ConvertTableToCloseCommand deviceAddress{" + deviceAddress + "} tableNumber{" + tableNumber + "}");
            return GenerateRelayCommand(tableNumber, isTurnOn: false);
        }
        private byte[] GenerateRelayCommand(int deviceAddress, bool isTurnOn)
        {
            int deviceType = 1; //1:8port mod bus 2:4port mod bus
            if (deviceType == 2) 
            {
                deviceAddress -= 1;
                //轉換規則:桌號即端口(4port)    
                byte addressByte = Convert.ToByte(deviceAddress / 4 + 1);      //電路板地址
                byte portByte = Convert.ToByte(deviceAddress % 4);  //串口地址     
                                                                    //byte addressByte = 01;          //電路板地址
                                                                    //byte portByte = deviceAddress ;  //串口地址          
                byte controlByte = isTurnOn ? (byte)0xFF : (byte)0x00; //開關

                byte[] data = new byte[]
                {
                addressByte, 0x05, 0x00, portByte, controlByte, 0x00
                };

                ushort crc = CalculateCRC16(data);

                byte[] fullCommand = new byte[data.Length + 2];
                Array.Copy(data, fullCommand, data.Length);

                fullCommand[data.Length] = (byte)(crc & 0xFF);
                fullCommand[data.Length + 1] = (byte)(crc >> 8);
                //_logger.LogInformation("API function GenrateRelayCommand addressByte{" + addressByte + "} controlCommand{" + controlCommand + "} Data[" + BitConverter.ToString(data).Replace("-", " ") + "] fullcommand: " + BitConverter.ToString(fullCommand).Replace("-", " "));
                return fullCommand;
            }
            else 
            {
                deviceAddress -= 1;
                //轉換規則:桌號即端口(8port)    
                byte addressByte = Convert.ToByte(1);      //電路板地址
                byte portByte = Convert.ToByte(deviceAddress);  //串口地址     
                                                                    //byte addressByte = 01;          //電路板地址
                                                                    //byte portByte = deviceAddress ;  //串口地址          
                byte controlByte = isTurnOn ? (byte)0xFF : (byte)0x00; //開關

                byte[] data = new byte[]
                {
                addressByte, 0x05, 0x00, portByte, controlByte, 0x00
                };

                ushort crc = CalculateCRC16(data);

                byte[] fullCommand = new byte[data.Length + 2];
                Array.Copy(data, fullCommand, data.Length);

                fullCommand[data.Length] = (byte)(crc & 0xFF);
                fullCommand[data.Length + 1] = (byte)(crc >> 8);
                //_logger.LogInformation("API function GenrateRelayCommand addressByte{" + addressByte + "} controlCommand{" + controlCommand + "} Data[" + BitConverter.ToString(data).Replace("-", " ") + "] fullcommand: " + BitConverter.ToString(fullCommand).Replace("-", " "));
                return fullCommand;
            }

        }


        public static ushort CalculateCRC16(byte[] data)
        {
            byte uchCRCHi = 0xFF; // CRC 的高字节初始化
            byte uchCRCLo = 0xFF; // CRC 的低字节初始化

            for (int i = 0; i < data.Length; i++)
            {
                int uIndex = uchCRCLo ^ data[i]; // 计算 CRC
                uchCRCLo = (byte)(uchCRCHi ^ auchCRCHi[uIndex]);
                uchCRCHi = auchCRCLo[uIndex];
            }

            return (ushort)(uchCRCHi << 8 | uchCRCLo);
        }
        private byte[] StringToByteArray(string hexString)
        {
            hexString = hexString.Replace(" ", ""); // 移除字串中的空格
            int byteCount = hexString.Length / 2;
            byte[] byteArray = new byte[byteCount];

            for (int i = 0; i < byteCount; i++)
            {
                string byteValue = hexString.Substring(i * 2, 2);
                byteArray[i] = Convert.ToByte(byteValue, 16);
            }

            return byteArray;
        }
        private static readonly byte[] auchCRCHi = {
        // 高位字节的 CRC 值
        0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0, 0x80, 0x41,
0x00, 0xC1, 0x81,
0x40, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81,
0x40, 0x01, 0xC0,
0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1,
0x81, 0x40, 0x01,
0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01,
0xC0, 0x80, 0x41,
0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40,
0x00, 0xC1, 0x81,
0x40, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80,
0x41, 0x01, 0xC0,
0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0,
0x80, 0x41, 0x01,
0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x00,
0xC1, 0x81, 0x40,
0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0, 0x80, 0x41,
0x00, 0xC1, 0x81,
0x40, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81,
0x40, 0x01, 0xC0,
0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1,
0x81, 0x40, 0x01,
0xC0, 0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x01,
0xC0, 0x80, 0x41,
0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41,
0x00, 0xC1, 0x81,
0x40, 0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81,
0x40, 0x01, 0xC0,
0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0,
0x80, 0x41, 0x01,
0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01,
0xC0, 0x80, 0x41,
0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0, 0x80, 0x41,
0x00, 0xC1, 0x81,
0x40

    };

        private static readonly byte[] auchCRCLo = {
        // 低位字节的 CRC 值
        0x00, 0xC0, 0xC1, 0x01, 0xC3, 0x03, 0x02, 0xC2, 0xC6, 0x06, 0x07, 0xC7,
0x05, 0xC5, 0xC4,
0x04, 0xCC, 0x0C, 0x0D, 0xCD, 0x0F, 0xCF, 0xCE, 0x0E, 0x0A, 0xCA, 0xCB,
0x0B, 0xC9, 0x09,
0x08, 0xC8, 0xD8, 0x18, 0x19, 0xD9, 0x1B, 0xDB, 0xDA, 0x1A, 0x1E, 0xDE,
0xDF, 0x1F, 0xDD,
0x1D, 0x1C, 0xDC, 0x14, 0xD4, 0xD5, 0x15, 0xD7, 0x17, 0x16, 0xD6, 0xD2,
0x12, 0x13, 0xD3,
0x11, 0xD1, 0xD0, 0x10, 0xF0, 0x30, 0x31, 0xF1, 0x33, 0xF3, 0xF2, 0x32,
0x36, 0xF6, 0xF7,
0x37, 0xF5, 0x35, 0x34, 0xF4, 0x3C, 0xFC, 0xFD, 0x3D, 0xFF, 0x3F, 0x3E,
0xFE, 0xFA, 0x3A,
0x3B, 0xFB, 0x39, 0xF9, 0xF8, 0x38, 0x28, 0xE8, 0xE9, 0x29, 0xEB, 0x2B,
0x2A, 0xEA, 0xEE,
0x2E, 0x2F, 0xEF, 0x2D, 0xED, 0xEC, 0x2C, 0xE4, 0x24, 0x25, 0xE5, 0x27,
0xE7, 0xE6, 0x26,
0x22, 0xE2, 0xE3, 0x23, 0xE1, 0x21, 0x20, 0xE0, 0xA0, 0x60, 0x61, 0xA1,
0x63, 0xA3, 0xA2,
0x62, 0x66, 0xA6, 0xA7, 0x67, 0xA5, 0x65, 0x64, 0xA4, 0x6C, 0xAC, 0xAD,
0x6D, 0xAF, 0x6F,
0x6E, 0xAE, 0xAA, 0x6A, 0x6B, 0xAB, 0x69, 0xA9, 0xA8, 0x68, 0x78, 0xB8,
0xB9, 0x79, 0xBB,
0x7B, 0x7A, 0xBA, 0xBE, 0x7E, 0x7F, 0xBF, 0x7D, 0xBD, 0xBC, 0x7C, 0xB4,
0x74, 0x75, 0xB5,
0x77, 0xB7, 0xB6, 0x76, 0x72, 0xB2, 0xB3, 0x73, 0xB1, 0x71, 0x70, 0xB0,
0x50, 0x90, 0x91,
0x51, 0x93, 0x53, 0x52, 0x92, 0x96, 0x56, 0x57, 0x97, 0x55, 0x95, 0x94,
0x54, 0x9C, 0x5C,
0x5D, 0x9D, 0x5F, 0x9F, 0x9E, 0x5E, 0x5A, 0x9A, 0x9B, 0x5B, 0x99, 0x59,
0x58, 0x98, 0x88,
0x48, 0x49, 0x89, 0x4B, 0x8B, 0x8A, 0x4A, 0x4E, 0x8E, 0x8F, 0x4F, 0x8D,
0x4D, 0x4C, 0x8C,
0x44, 0x84, 0x85, 0x45, 0x87, 0x47, 0x46, 0x86, 0x82, 0x42, 0x43, 0x83,
0x41, 0x81, 0x80,
0x40

    };





       

        









    }
}
