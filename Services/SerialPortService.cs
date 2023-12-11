using Microsoft.Extensions.Logging;
using System;
using System.IO.Ports;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Data;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Bson;
using MahJongBPS;
using MahJongBPS.Controllers;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Runtime.CompilerServices;
using static System.Net.Mime.MediaTypeNames;

namespace MahJongBPS.Services
{

    public interface ISerialPortService
    {
        void OnApplicationStopping();
        void OpenPort();
        void ClosePort();
        void PS100Write(string data);
        void XC100Write(string command, int amount);
        Task MHWrite(string data);
        event EventHandler<string> DataReceived;
        void Checkout_1();
        void Checkout_2(int CheckoutAmount);
        void Checkout_3();
        void XC100StockUpdate(int amount, int method);
        int XC100StockScan();
        Task MHdispense(int data);
        void recipt(Int64 OrderId,string TableName, int TableId, Decimal Hours, int Amount, DateTime dateTime, int H);
        void MHstockUpdate(int amount, int method);
        void testrecipt();
    }

    public class SerialPortService : ISerialPortService
    {
        private SerialPort PS100_serialPort;    //PS100板子
        private SerialPort XC100_serialPort;    //出鈔機
        private SerialPort MH_serialPort;       //出硬機
        private SerialPort WP_K837_serialPort;  //印單機

        private readonly ILogger<SerialPortService> _logger;
        public event EventHandler<string> CheckoutCompleted;
        public event EventHandler<string> DataReceived;
        private readonly IHubContext<NotificationHub> _notificationHubContext;
        private Dictionary<string, HardwareCommands> fullCommandsMap = new Dictionary<string, HardwareCommands>();
        private readonly TableController _tablecontroller;
        private readonly IHostApplicationLifetime _lifetime;

        public SerialPortService(string PS100PortName,string XC100PortName, string PrinterPortName,string HopperPortName, ILogger<SerialPortService> logger, IHubContext<NotificationHub> notificationHubContext, TableController tableController, IHostApplicationLifetime lifetime)
        {
            _logger = logger;//注入log
            _notificationHubContext = notificationHubContext; // 注入通知上下文
            _tablecontroller = tableController; //注入TableController
            _lifetime = lifetime;
            _lifetime.ApplicationStopping.Register(OnApplicationStopping);

            //初始化PS100設定
            PS100_serialPort = new SerialPort(PS100PortName, 9600);
            PS100_serialPort.DataReceived += SerialPortDataReceived;
            //初始化XC100串口設定
            if (XC100PortName != "COM")
            {
                XC100_serialPort = new SerialPort(XC100PortName);
                XC100_serialPort.BaudRate = 9600;
                XC100_serialPort.Parity = Parity.None;
                XC100_serialPort.DataBits = 8;
                XC100_serialPort.StopBits = StopBits.One;
                XC100_serialPort.DataReceived += XC100_serialPort_DataReceived;
            }
            //初始化Mini Hopper串口設定
            if (HopperPortName != "COM")
            {
                MH_serialPort = new SerialPort(HopperPortName, 9600);
                MH_serialPort.DataReceived += MH_serialPort_DataReceived;
            }
                       
            //初始化WP-K837崁入式印表機串口設定
            if (PrinterPortName != "COM")
            {
                WP_K837_serialPort = new SerialPort(PrinterPortName);
                WP_K837_serialPort.BaudRate = 9600;
            }

            //初始化服務設定
            try
            {
                OpenPort();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            //初始化指令描述
            string jsonFilePath = "config/command.json"; // 請替換成 command.json 的實際路徑
            string jsonContent = File.ReadAllText(jsonFilePath);
            JObject commandData = JObject.Parse(jsonContent);
            LoadCommandsFromJson(jsonFilePath);
            Console.WriteLine("Service here");
        }
        public void OnApplicationStopping()
        {
            // 在应用程序停止时执行清理操作
            _lifetime.StopApplication();
            // 关闭串口、保存状态等
        }

       
        public void OpenPort()
        {

            PS100_serialPort.Open();
            if (XC100_serialPort != null)
            {
                XC100_serialPort.Open();
            }
            if (WP_K837_serialPort != null)
            {
                WP_K837_serialPort.Open();
            }
            if(MH_serialPort != null)
            {
                MH_serialPort.Open();
                MHWrite("80");
            }

        }
        
        public void ClosePort()
        {
            PS100_serialPort.Close();
            if (XC100_serialPort != null)
            {
                XC100_serialPort.Close();
            }
            if (WP_K837_serialPort != null)
            {
                WP_K837_serialPort.Close();
            }
            if (MH_serialPort != null)
            {
                MH_serialPort.Close();
            }
        }


        public void XC100Write(string command, int amount)
        {

            // 构建数据帧
            // 出鈔指令
            string data = amount.ToString().PadLeft(3, '0'); // 将amount转换为三位数的字符串，不足三位用零填充
            byte[] dataBytes = new byte[10]; // 数据帧总共有10个字节

            // 添加起始码 (STX)
            dataBytes[0] = 0x02;

            // 添加ID_10和ID_1，这里默认都是0
            dataBytes[1] = 0x30; // ID_10
            dataBytes[2] = 0x30; // ID_1

            // 添加指令 (CMD)
            dataBytes[3] = (byte)command[0];
            dataBytes[4] = 0x30;
            // 添加数据字段 (DATA1 DATA2 DATA3)
            for (int i = 0; i < 3; i++)
            {
                dataBytes[5 + i] = (byte)data[i];
            }

            // 计算校验码 (CS)
            byte checksum = 0;
            for (int i = 0; i < dataBytes.Length - 2; i++) // 从第1个字节到倒数第4个字节计算校验码
            {
                checksum += dataBytes[i];
            }
            checksum = (byte)(checksum % 256); // 取校验码的后两位
            dataBytes[8] = checksum; // 添加校验码

            // 添加结束码 (ETX)
            dataBytes[9] = 0x03;

            if (XC100_serialPort.IsOpen)
            {
                try
                {
                    XC100_serialPort.Write(dataBytes, 0, dataBytes.Length);
                    _logger.LogInformation($"XC100 寫入: {BitConverter.ToString(dataBytes)}");
                }
                catch
                {
                    _logger.LogInformation("XC 100 failed to write");
                }
            }
            else
            {
                // 可以在串口未打开时进行适当的错误处理或记录日志
                // 例如，记录日志并抛出异常，或者发送通知等
                _logger.LogError("串口未打开，无法发送数据");
            }
        }
        int ReceivedAmount;
        int CoinReceivedAmount;
        //購買按鈕
        public void Checkout_1()
        {

            _logger.LogInformation("\r\n");
            _logger.LogInformation("開始現金接收");
            _logger.LogInformation("\r\n");
            
            //開啟現金設備
            PS100Write(GenerateFullCommand("04", 0, 0));      //開啟紙鈔機
            System.Threading.Thread.Sleep(250);
            PS100Write(GenerateFullCommand("03", 0, 0));      //開啟投幣器
            //System.Threading.Thread.Sleep(250);
            //PS100Write(GenerateFullCommand("02", 0, 0));      //獲取暫存器狀態

            //退款金額歸零
            PayoutValue = 0;
            //已投入金額歸零
            ReceivedAmount = 0;         //紙鈔計數歸零
            CoinReceivedAmount = 0;     //硬幣計數歸零
            //投錢狀態歸零
            CoinAmount = 0;
            PaperAmount = 0;
            TotalAmount = 0;

        }
        int PayoutValue = 0;
        bool CoinComplete = false; //確認投幣機是否正確關閉-若沒有完全關閉就無法出幣
        //結帳按鈕
        public async void Checkout_2(int CheckoutAmount)
        {
            CoinComplete = false;
            PS100Write(GenerateFullCommand("05", 0, 0));      //關閉投幣器
            //等待CoinComplete 為true才能繼續
            if(CoinReceivedAmount > 0) //如果有投幣等待設置HOPPER的訊息
            {
                int WaitState = 0;
                while (!CoinComplete)
                {
                    if (WaitState >= 250)
                    {
                        CoinComplete = true;
                        _logger.LogError("[P20001]硬幣機訊息回傳逾時");
                    }
                    WaitState += 1;
                    //Console.Write(".");
                    await Task.Delay(100);
                    
                }
                
                _logger.LogInformation($"[0x08] 回應時間花費: {WaitState*100}毫秒");

            }
            else
            {
                System.Threading.Thread.Sleep(250);
            }
            //設定出鈔的設備 1:PS100 2:XC100
            //設定出幣的設備 1:PS100 2:Mini Hopper
            int PaperMode = 1;
            int CoinMode = 1;
            //計算找零金額
            int PayoutAmount = ReceivedAmount + CoinReceivedAmount - CheckoutAmount;
            PayoutValue = PayoutAmount;
            _logger.LogInformation("\r\n");
            _logger.LogInformation($"開始結帳 找零 訂單金額:{CheckoutAmount}元");
            _logger.LogInformation($"         投入金額:{ReceivedAmount + CoinReceivedAmount}元");
            _logger.LogInformation($"         應找金額:{PayoutAmount}元");
            _logger.LogInformation("\r\n");

            PS100Write(GenerateFullCommand("02", 0, 0));      //獲取暫存器狀態

            System.Threading.Thread.Sleep(250);
            //_logger.LogInformation($"PayoutAmount:{PayoutAmount},ReceivedAmount:{ReceivedAmount} ,CoinReceivedAmount:{CoinReceivedAmount} ,CheckoutAmount:{CheckoutAmount}");
            //驗證是否已完成找零 -- 若 找零狀態 == 找零金額 就傳送 "結帳完成"
            if (TotalAmount == PayoutValue)
            {
                OnCheckoutCompleted(this, "結帳完成");
            }
            //出鈔 
            int PayoutPaper = (PayoutAmount / 100);
            int PayoutCoin = 0;
            if (PayoutPaper > 0)
            {

                //_logger.LogInformation($"找鈔 {PayoutPaper * 100}元");
                if (PaperMode == 1)       //--使用PS100出鈔
                {
                    int Payout500 = PayoutPaper / 5;
                    int Payout100 = PayoutPaper % 5;
                    if (Payout500 > catche500)
                    {
                        _logger.LogInformation("500鈔 儲存量不足");
                        Payout100 += (Payout500-catche500) * 5;
                        Payout500 = catche500;
                    }
                    if(Payout100 > catche100)
                    {
                        PayoutCoin += (Payout100 - catche100) * 100;
                        Payout100 = catche100;
                        _logger.LogInformation($"100鈔 儲存量不足 出幣遞補{PayoutCoin}"); 
                    }
                    PayoutPaper = Payout100 + Payout500*5;
                    //_logger.LogInformation($"PS100暫存器 100元: {catche100}張    500: {catche500}張");
                    //_logger.LogInformation($"PS100出鈔   100元: {Payout100}張    500: {Payout500}張");
                    _logger.LogInformation($"PS100 寫入:[出鈔]{PayoutPaper}元" + "\r\n");
                    PS100Write(GenerateFullCommand("10", PayoutPaper * 100, 0));
                    System.Threading.Thread.Sleep(1000);
                }
                else if (PaperMode == 2)   //--使用XC100出鈔
                {
                    if (PayoutPaper > XC100Stock)
                    {
                        PayoutCoin += (PayoutPaper - XC100Stock) * 100;
                        PayoutPaper = XC100Stock;
                        _logger.LogInformation("鈔票儲存量不足找零金額");
                    }
                    _logger.LogInformation($"XC100 寫入:[出鈔]{PayoutPaper * 100}元" + "\r\n");
                    XC100Write("B", PayoutPaper);

                }
            }
            //出幣
            PayoutCoin += PayoutAmount % 100;
            if (PayoutCoin > 0)
            {
                if(CoinMode == 1)
                {
                    PS100Write(GenerateFullCommand("11", PayoutCoin, 0));
                    _logger.LogInformation($"PS100 寫入:[出幣]{PayoutCoin}元" + "\r\n");
                    System.Threading.Thread.Sleep(1000);
                }else if(CoinMode == 2)
                {
                    MHdispense(PayoutCoin/10);
                    _logger.LogInformation($"MH-3  寫入:[出幣]{PayoutCoin}元" + "\r\n");
                }

            }
            
            PS100Write(GenerateFullCommand("06", 0, 0));        //關閉紙鈔機
            //已投入金額歸零
            ReceivedAmount = 0;
            CoinReceivedAmount = 0;
            //投錢狀態歸零
            CoinAmount = 0;
            PaperAmount = 0;
            TotalAmount = 0;
        }
        //取消按鈕
        public void Checkout_3()
        {

            _logger.LogInformation("\r\n");
            _logger.LogInformation( "取消結帳");
            _logger.LogInformation("\r\n");

            //_logger.LogInformation($"ReceivedAmount:{ReceivedAmount} ,CoinReceivedAmount:{CoinReceivedAmount}");

            if (ReceivedAmount > 0)
            {
                int PayoutPaper = (ReceivedAmount / 100);
                XC100Write("B", PayoutPaper);                                     //出鈔
                _logger.LogInformation($"退款 100元: {PayoutPaper}張");
            }
            if (CoinReceivedAmount > 0)
            {
                PS100Write(GenerateFullCommand("11", CoinReceivedAmount, 0)); //出幣
                _logger.LogInformation($"退款 {CoinReceivedAmount}元");
            }
            System.Threading.Thread.Sleep(250);
            PS100Write(GenerateFullCommand("05", 0, 0));                    //關閉投幣器
            System.Threading.Thread.Sleep(250);
            PS100Write(GenerateFullCommand("06", 0, 0));                    //關閉紙鈔機
            //已投入金額歸零
            ReceivedAmount = 0;
            CoinReceivedAmount = 0;
            //找錢狀態歸零
            CoinAmount = 0;
            PaperAmount = 0;
            TotalAmount = 0;
        }

        int XC100Stock = 0;
        //更新XC100儲量
        public void XC100StockUpdate(int amount, int Method)
        {

            if (Method == 1)
            {
                XC100Stock += amount;
            }
            else
            {
                XC100Stock = amount;
            }
            _logger.LogInformation($"已更新鈔票儲存量: {XC100Stock}張");
        }

        public int XC100StockScan()
        {
            _logger.LogInformation($"XC100 剩餘: {XC100Stock}張");
            return XC100Stock;
        }

        public async void PS100Write(string data)
        {
            if (PS100_serialPort.IsOpen)
            {
                _logger.LogInformation($"PS100 寫入:[{data}]");
                byte[] dataToSend = StringToByteArray(data);
                try
                {
                    PS100_serialPort.Write(dataToSend, 0, dataToSend.Length);
                }
                catch
                {
                    _logger.LogInformation("PS100 failed to write");
                }
            }
            else
            {
                _logger.LogError("串口異常,無法打開");
            }
        }
        
        private void MH_serialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            byte[] dataBytes = ReadDataAsBytes(MH_serialPort);
            string data = string.Join(" ", dataBytes.Select(b => b.ToString("X2"))); // 使用空格分隔并转为十六进制字符串
            _logger.LogInformation(data);
            string accept = "02";
            string reject = "0F";
            string dispense = "10";
            string cancel = "11";
            string completed = "3E";
            if (data == accept)
            {
                MHWrite(dispense);
            }
            else if (data == reject)
            {
                MHWrite(cancel);
            }else if(data == completed)
            {
                DispenseCompleted = true;
                Console.WriteLine("DispenseCompleted = true;");
            }
        }

        private string XC100buffer;
        private void XC100_serialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            byte[] dataBytes = ReadDataAsBytes(XC100_serialPort);
            string data = string.Join(" ", dataBytes.Select(b => b.ToString("X2"))); // 使用空格分隔并转为十六进制字符串
            XC100buffer += data + " ";
            if (XC100buffer.Contains("02") && XC100buffer.Contains("03"))
            {
                _logger.LogInformation($"XC100 回傳: {XC100buffer}");
                string commandByte = XC100buffer.Substring(9, 2);
                string dataByte1 = XC100buffer.Substring(22, 1);
                string dataByte2 = XC100buffer.Substring(19, 1);
                string dataByte3 = XC100buffer.Substring(16, 1);
                string dataString = "";

                if (dataByte3 != "0")
                {
                    dataString += dataByte3;
                    dataString += dataByte2;
                    dataString += dataByte1;

                }
                else if (dataByte2 != "0")
                {
                    dataString += dataByte2;
                    dataString += dataByte1;
                }
                else
                {
                    dataString += dataByte1;
                }

                switch (commandByte)
                {
                    case "62":
                        _logger.LogInformation($"XC100 出鈔: {dataString}次");
                        XC100Stock -= Int32.Parse(dataString);
                        _logger.LogInformation($"XC100 剩餘: {XC100Stock}張");
                        break;
                }
                XC100buffer = "";
            }
            else if (XC100buffer.Contains("06"))
            {
                _logger.LogInformation($"XC100 回傳: {XC100buffer}");
                XC100buffer = "";
            }
        }

        int MHstock = 0;
        public void MHstockUpdate(int amount , int method)
        {
            if (method == 1)
            {
                MHstock += amount;
            }
            else
            {
                MHstock = amount;
            }
            _logger.LogInformation($"已更新硬幣儲存量: {MHstock}枚");
        }

        bool DispenseCompleted = false;
        public async Task MHdispense(int data)
        {
            DispenseCompleted = false;
            if (data <= 10 && data >= 1)
            {
                MHWrite("81");
                Thread.Sleep(100);
                MHWrite((data+39).ToString());
                _logger.LogInformation($"出幣機出幣:{data}枚");
                DispenseCompleted = false;
                while (!DispenseCompleted)
                {
                    //waitForCheckoutCompletion = true;// <---------------這行之後要註解掉 他決定了網頁是否要等待錢箱結帳完成
                    //await new Promise(resolve => setTimeout(resolve, 100)); // 等待100毫秒后再次检查标志变量
                    Console.Write(".");
                    await Task.Delay(100);
                }
                MHstock -= data;
                _logger.LogInformation($"完成出幣:10枚  出幣機剩餘:{MHstock}枚");

                CoinAmount += data * 10;
                TotalAmount = PaperAmount + CoinAmount; 
                OnCashedOut(this, data * 10);
                if (TotalAmount == PayoutValue)
                {
                    OnCheckoutCompleted(this, "結帳完成");
                }

            }
            else if(data > 10)
            {
                _logger.LogInformation($"總共出幣:{data}枚");
                DispenseCompleted = false;
                int j = (data / 10);
                for (int i = 0; i <  j; i++)
                {
                    MHWrite("81");
                    Thread.Sleep(100);
                    MHWrite("49");
                    _logger.LogInformation($"出幣機出幣:10枚");
                    DispenseCompleted = false;
                    while (!DispenseCompleted)
                    {
                        //waitForCheckoutCompletion = true;// <---------------這行之後要註解掉 他決定了網頁是否要等待錢箱結帳完成
                        //await new Promise(resolve => setTimeout(resolve, 100)); // 等待100毫秒后再次检查标志变量
                        Console.Write(".");
                        await Task.Delay(100);
                    }
                    MHstock -= 10;
                    _logger.LogInformation($"完成出幣:10枚  出幣機剩餘:{MHstock}枚");
                    CoinAmount += 10 * 10;
                    TotalAmount = PaperAmount + CoinAmount;
                    OnCashedOut(this, 10 * 10);

                    if (TotalAmount == PayoutValue)
                    {
                        OnCheckoutCompleted(this, "結帳完成");
                    }

                }
                data = data % 10;
                MHWrite("81");
                Thread.Sleep(100);
                MHWrite((data + 39).ToString());
                _logger.LogInformation($"出幣機出幣:{data}枚");
                DispenseCompleted = false;
                while (!DispenseCompleted)
                {
                    //waitForCheckoutCompletion = true;// <---------------這行之後要註解掉 他決定了網頁是否要等待錢箱結帳完成
                    //await new Promise(resolve => setTimeout(resolve, 100)); // 等待100毫秒后再次检查标志变量
                    Console.Write(".");
                    await Task.Delay(100);
                }
                MHstock -= data;
                _logger.LogInformation($"完成出幣:10枚  出幣機剩餘:{MHstock}枚");
                CoinAmount += data * 10;
                TotalAmount = PaperAmount + CoinAmount; 
                OnCashedOut(this, data * 10);
                if (TotalAmount == PayoutValue)
                {
                    OnCheckoutCompleted(this, "結帳完成");
                }

            }
            else
            {
                _logger.LogInformation($"出幣機出幣:[輸入數必須大於一]");
            }
            
        }
        public async Task MHWrite(string data)
        {
            
            if (MH_serialPort.IsOpen)
            {
                _logger.LogInformation($"Mini Hopper 寫入:[{data}]");
                byte[] dataToSend = StringToByteArray(data);
                try
                {
                    MH_serialPort.Write(dataToSend, 0, dataToSend.Length);
                }
                catch
                {
                    _logger.LogInformation("PS100 failed to write");
                }
            }
            else
            {
                _logger.LogError("串口異常,無法打開");
            }
            
        }

        private  string receiveBuffer = "";
        int CheckOutValue = 0;
        private void SerialPortDataReceived(object sender, SerialDataReceivedEventArgs e)
        {

            byte[] dataBytes = ReadDataAsBytes(PS100_serialPort);
            string data = Encoding.GetEncoding("ISO-8859-1").GetString(dataBytes);
            receiveBuffer += data;
            while (receiveBuffer.Contains("@") && receiveBuffer.Contains("~"))
            {
                string completeData = receiveBuffer.Substring(receiveBuffer.IndexOf("@"));
                int endIndex = receiveBuffer.IndexOf("~");
                //轉十六進制
                string hexData = ConvertToHex(completeData);

                //獲得訊息來源
                if (hexData.Length > 6)
                {
                    string commandSource = hexData.Substring(6, 2);
                    //獲得訊息類型
                    string commandByte = hexData.Substring(8, 2);
                    //格式化訊息
                    string formattedData = FormatHexData(hexData);
                    
                    //string commandString;
                    _logger.LogInformation($"PS100 回傳:[0x{commandByte}][{commandSource}]:[{formattedData}]");

                    //判斷訊息來源是否為PS100
                    if (commandSource == "01")
                    {
                        response(commandByte, commandSource);
                        //判斷指令類型
                        switch (commandByte)
                        {
                            case "08":
                                SetCoin(hexData);
                                break;
                            case "11":
                                CoinOut(hexData);
                                break;
                            case "22":
                                UpdatePaperData(hexData);
                                break;
                            case "23":
                                UpdateCoinData(hexData);
                                break;
                            case "24":
                                ReadPaper(hexData);
                                break;
                            case "25":
                                ReadCoin(hexData);
                                break;
                            case "C0":
                                CoinCount(hexData);
                                break;
                            case "C1":
                                PaperCount(hexData);
                                break;
                            default:
                                // 未知的訊息類型，不做任何處理
                                break;
                        }
                    }
                    else
                    {
                        return;
                    }
                }
                else
                {
                    _logger.LogInformation($"PS100 回傳:{hexData}");
                }




                // await ProcessReceivedDataAsync(completeData);
                if (hexData.Contains("4005000101EC0104087F7E"))
                {
                    CheckOutValue += 1;
                    _logger.LogInformation("CheckOutValue +1");
                }
                if (hexData.Contains("4005000101EC0103097F7E"))
                {
                    CheckOutValue += 1;
                    _logger.LogInformation("CheckOutValue +1");
                }
                if (CheckOutValue == 2)
                {
                    CheckOutValue = 0;
                    PS100Write("40 02 00 00 06 F8 7F 7E");//關閉紙鈔機

                    OnCheckoutCompleted(this, "結帳完成");
                }
                // 从缓冲区中移除已处理的数据
                receiveBuffer = receiveBuffer.Substring(endIndex + 1);


                // 继续查找下一个数据开始标记
                receiveBuffer = receiveBuffer.Substring(receiveBuffer.IndexOf("~") + 1);
            }
            // 使用日志记录器输出数据


            // 触发事件以通知其他部分
            DataReceived?.Invoke(this, data);
        }

        private void OnCheckoutCompleted(object sender, string message)
        {
            _logger.LogInformation(message + "\r\n");
            // 通知 NotificationHub
            _notificationHubContext.Clients.All.SendAsync("CheckoutCompleted", message);
        }
        private void OnRecivedCash(object sender,int RecivedCash)
        {
            //_logger.LogInformation($"收錢: {RecivedCash}元" + "\r\n");
            if (RecivedCash >= 100)
            {
                _logger.LogInformation($"      收鈔            一張{RecivedCash}元" + "\r\n");
            }
            else
            {
                _logger.LogInformation($"      收幣 一枚{RecivedCash}元" + "\r\n");

            }
            // 通知 NotificationHub
            _notificationHubContext.Clients.All.SendAsync("RecviedCash", RecivedCash);
        }

        private void OnCashedOut(object send,int CashedAmount)
        {
            //_logger.LogInformation($"找錢: {CashedAmount}元" + "\r\n");
            if(CashedAmount >= 100)
            {
                _logger.LogInformation($"      出鈔            一張{CashedAmount}元" + "\r\n");
            }
            else
            {
                _logger.LogInformation($"      出幣            一枚{CashedAmount}元" + "\r\n");

            }
            // 通知 NotificationHub
            _notificationHubContext.Clients.All.SendAsync("PayoutCash", CashedAmount);
        }

        private void OnPaperVerifing(object sender, bool Verify)
        {
            if (Verify)
            {
                _logger.LogInformation($"[紙鈔驗證中]");
            }
            else
            {
                _logger.LogInformation($"[紙鈔驗證完成]");
            }
            _notificationHubContext.Clients.All.SendAsync("PaperVerifying",Verify);
        }

        private byte[] ReadDataAsBytes(SerialPort port)
        {
            byte[] buffer = new byte[port.BytesToRead];
            port.Read(buffer, 0, buffer.Length);
            return buffer;
        }

        private string ConvertToHex(string data)
        {
            StringBuilder hex = new StringBuilder(data.Length * 2);
            foreach (char c in data)
            {
                hex.Append(((byte)c).ToString("X2"));
            }
            return hex.ToString();
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
        private string FormatHexData(string hexData)
        {
            StringBuilder formattedData = new StringBuilder();
            int count = 0;
            foreach (char c in hexData)
            {
                formattedData.Append(c);
                count++;
                if (count % 2 == 0)
                {
                    formattedData.Append(" ");
                }
            }
            return formattedData.ToString().TrimEnd();
        }

        private void response(string data, string commandSource)
        {
            string selectedHardware = "PS100 中文";
            string shortCommand = "0x" + data;

            if (fullCommandsMap.ContainsKey(selectedHardware) &&
                fullCommandsMap[selectedHardware].Commands.ContainsKey(shortCommand))
            {
                string description = fullCommandsMap[selectedHardware].Commands[shortCommand];
                string formattedData = $"PS100 回傳:[{shortCommand}][{commandSource}]:[{description}]";
                _logger.LogInformation($"{formattedData}\r\n");
            }
        }
        bool state11 = false,state02 = false;
        private void SetCoin(string data)   //0x08
        {
            string state = data.Substring(10, 2);
            if (state == "02")
            {
                CoinComplete = true;
                
            }
           
        }
        private void CoinOut(string data) //0x11
        {
            string state =data.Substring(14, 2);
            if(state == "E1")
            {
                _logger.LogError("[P00001]PS100出幣回傳錯誤訊息");
            }
            else if(state =="E2")
            {
                _logger.LogError("[P00001]PS100出幣回傳錯誤訊息");
            }                          
        }
        private void ReadCoin(string data)
        {
            if (data.Length >= 28) // 確保資料長度足夠
            {
                // 解析硬幣機收幣資訊，假設資料長度為 28 個字元
                int oneYuan = Convert.ToInt32(data.Substring(14, 2) + data.Substring(12, 2), 16);
                int fiveYuan = Convert.ToInt32(data.Substring(18, 2) + data.Substring(16, 2), 16);
                int tenYuan = Convert.ToInt32(data.Substring(22, 2) + data.Substring(20, 2), 16);
                int fiftyYuan = Convert.ToInt32(data.Substring(26, 2) + data.Substring(24, 2), 16);
                int sum = oneYuan + fiveYuan * 5 + tenYuan * 10 + fiftyYuan * 50;

                // 將硬幣數量顯示在 richTextBox2
                string formattedData = $"硬幣機內硬幣數目 目前{sum}元  一元:{oneYuan}枚 五元:{fiveYuan}枚 十元:{tenYuan}枚 五十元:{fiftyYuan}枚";

                _logger.LogInformation(formattedData);

            }
        }

        int catche100 = 0;
        int catche500 = 0;
        private void ReadPaper(string data)
        {
            catche100 = Convert.ToInt32(data.Substring(12, 2), 16);
            catche500 = Convert.ToInt32(data.Substring(20, 2), 16);
            int oneHundred = Convert.ToInt32(data.Substring(52, 2), 16);
            int fiveHundred = Convert.ToInt32(data.Substring(60, 2), 16);
            int oneThousand = Convert.ToInt32(data.Substring(64, 2), 16);
            string formattedData = $"目前紙鈔暫存器 目前元  一百元:{catche100} 五百元:{catche500}" + "\r\n" + $"               目前紙鈔箱 目前元  一百元:{oneHundred} 五百元:{fiveHundred} 一千元:{oneThousand}";
            _logger.LogInformation(formattedData);
        }

        private void UpdateCoinData(string data)    //0x23收幣狀態
        {
            if (data.Length >= 28) // 確保資料長度足夠
            {
                // 解析硬幣機收幣資訊，假設資料長度為 28 個字元
                int oneYuan = Convert.ToInt32(data.Substring(14, 2) + data.Substring(12, 2), 16);
                int fiveYuan = Convert.ToInt32(data.Substring(18, 2) + data.Substring(16, 2), 16);
                int tenYuan = Convert.ToInt32(data.Substring(22, 2) + data.Substring(20, 2), 16);
                int fiftyYuan = Convert.ToInt32(data.Substring(26, 2) + data.Substring(24, 2), 16);
                int sum = oneYuan + fiveYuan * 5 + tenYuan * 10 + fiftyYuan * 50;

                // 將硬幣數量顯示在 richTextBox2
                string formattedData = $"投幣機收幣 目前{sum}元  一元:{oneYuan}枚 五元:{fiveYuan}枚 十元:{tenYuan}枚 五十元:{fiftyYuan}枚";
                _logger.LogInformation(formattedData);
                OnRecivedCash(this, sum-CoinReceivedAmount);
                CoinReceivedAmount = sum;
                // 在 UI 執行緒上更新 richTextBox2
                
            }
        }

        int BeforeAmount;
        int AfterAmount;
        bool before = false, after = false;
        private void UpdatePaperData(string data)   //0x22收鈔狀態
        {

            string StateByte = data.Substring(10, 2);
            string State = "";

            switch (StateByte)
            {
                case "01":  //收鈔完畢
                    State = "收鈔後";
                    break;
                case "02":  //收鈔辨識中
                    State = "收鈔前";
                    break;
            }

            if (data.Length >= 28) // 確保資料長度足夠
            {
                int currentCatche100 = Convert.ToInt32(data.Substring(12, 2), 16);
                int currentCatche500 = Convert.ToInt32(data.Substring(16, 2), 16);
                int oneHundred = Convert.ToInt32(data.Substring(32, 2), 16);
                int fiveHundred = Convert.ToInt32(data.Substring(36, 2), 16);
                int oneThousand = Convert.ToInt32(data.Substring(38, 2), 16);
                
                if (StateByte == "01")
                {
                    AfterAmount = oneHundred * 100 + fiveHundred * 500 + oneThousand * 1000 + currentCatche100 * 100 + currentCatche500 * 500;
                    string formattedData = $"[{State}]目前收鈔紙鈔暫存器      一百元:{currentCatche100} 五百元:{currentCatche500}" + "\r\n" + $"                       目前紙鈔箱 目前{AfterAmount}元  一百元:{oneHundred} 五百元:{fiveHundred} 一千元:{oneThousand}";
                    OnPaperVerifing(this, false);
                    _logger.LogInformation(formattedData);
                    if (before = true)    //驗證使否卡鈔
                    {
                        after = true;
                    }
                }
                else if (StateByte == "02")
                {
                    BeforeAmount = oneHundred * 100 + fiveHundred * 500 + oneThousand * 1000 + currentCatche100 * 100 + currentCatche500 * 500;
                    string formattedData = $"[{State}]目前紙鈔暫存器         一百元:{currentCatche100} 五百元:{currentCatche500}" + "\r\n" + $"                       目前紙鈔箱 目前{BeforeAmount}元  一百元:{oneHundred} 五百元:{fiveHundred} 一千元:{oneThousand}";
                    OnPaperVerifing(this, true);
                    _logger.LogInformation(formattedData);
                    before = true;
                }
                if (before && after)
                {
                    ReceivedAmount += AfterAmount - BeforeAmount;
                    _logger.LogInformation("\r\n");
                    _logger.LogInformation(  $"收款金額:{ReceivedAmount} 收款前:{BeforeAmount} 收款後:{AfterAmount}" );
                    OnRecivedCash(this, AfterAmount - BeforeAmount);
                    AfterAmount = 0;
                    BeforeAmount = 0;
                    before = false;
                    after = false;
                }
            }
        }

        int TotalAmount = 0;
        int CoinAmount = 0;
        int PaperAmount = 0;
        DateTime? CoinOutTimer;
        private void CoinCount(string data)     //0xC0出幣狀態
        {
            if (CoinAmount == 0)
            {
                var timeElapsed = DateTime.Now - CoinOutTimer.Value;
                _logger.LogInformation($"[0xC0] 回應時間花費: {timeElapsed.TotalMilliseconds}毫秒]");
            }
            if ( data.Length != 34) //2023/11/23 錯誤狀況判斷
            {

                _logger.LogInformation($"{data}");
                _logger.LogError($"錯誤代碼[P10001]:出幣回應格式有誤");

            }
            else
            {

                CoinAmount = ((Convert.ToInt32(data.Substring(20, 2), 16)) + (Convert.ToInt32(data.Substring(22, 2), 16) * 256) + (Convert.ToInt32(data.Substring(24, 2), 16) * 256 * 256) + (Convert.ToInt32(data.Substring(26, 2), 16) * 256 * 256 * 256)) / 100;
                int Amount = ((Convert.ToInt32(data.Substring(12, 2), 16)) + (Convert.ToInt32(data.Substring(14, 2), 16) * 256) + (Convert.ToInt32(data.Substring(16, 2), 16) * 256 * 256)) / 100;

                TotalAmount = PaperAmount + CoinAmount;
                _logger.LogInformation($"PS100 出幣:[0xC0]     {Amount}元一枚  目前累計找錢金額:{TotalAmount}元" );
                OnCashedOut(this, Amount);
                //驗證是否已完成找零 -- 若 找零狀態 == 找零金額 就傳送 "結帳完成"
                if (TotalAmount == PayoutValue)
                {
                    OnCheckoutCompleted(this, "結帳完成");
                }
            }
        }

        private void PaperCount(string data)    //0xC1出鈔狀態    
        {
            if (data.Length != 34)
            {
                _logger.LogInformation($"{data}");
                _logger.LogError($"錯誤代碼[P10002]:出鈔回應格式有誤");
            }
            else
            {
                PaperAmount = ((Convert.ToInt32(data.Substring(20, 2), 16)) + (Convert.ToInt32(data.Substring(22, 2), 16) * 256) + (Convert.ToInt32(data.Substring(24, 2), 16) * 256 * 256) + (Convert.ToInt32(data.Substring(26, 2), 16) * 256 * 256 * 256)) / 100;
                int Amount = ((Convert.ToInt32(data.Substring(12, 2), 16)) + (Convert.ToInt32(data.Substring(14, 2), 16) * 256) + (Convert.ToInt32(data.Substring(16, 2), 16) * 256 * 256)) / 100;
                TotalAmount = PaperAmount + CoinAmount;
                _logger.LogInformation($"PS100 出鈔:[0xC1]    {Amount}元一張  目前累計找錢金額:{TotalAmount}元" + "\r\n");
                OnCashedOut(this, Amount);
                //驗證是否已完成找零 -- 若 找零狀態 == 找零金額 就傳送 "結帳完成"
                if (TotalAmount == PayoutValue)
                {
                    OnCheckoutCompleted(this, "結帳完成");
                }
            }
                
        }

        private void LoadCommandsFromJson(string filePath)
        {
            try
            {
                // 讀取 JSON 檔案內容
                string jsonContent = File.ReadAllText(filePath);

                // 解析 JSON 檔案內容
                dynamic jsonObj = JsonConvert.DeserializeObject(jsonContent);

                // 確保 JSON 檔案包含 HardwareSpecs 屬性
                if (jsonObj != null && jsonObj.HardwareSpecs != null)
                {
                    foreach (var hardwareSpec in jsonObj.HardwareSpecs)
                    {
                        string hardwareName = hardwareSpec.Name.ToString();
                        var hardwareCommands = hardwareSpec.Commands.ToObject<Dictionary<string, string>>();

                        // 建立 HardwareCommands 物件
                        var hardware = new HardwareCommands { Commands = hardwareCommands };

                        // 將硬體規格名稱及對應的指令映射加入到 fullCommandsMap 字典中
                        fullCommandsMap.Add(hardwareName, hardware);

                    }
                }
                else
                {
                    // 若 JSON 檔案不包含 HardwareSpecs 屬性，可能需要處理其他情況
                    // 例如檔案格式不符合預期的錯誤處理
                }
            }
            catch (Exception ex)
            {
                // 處理讀取與解析 JSON 檔案時的錯誤
                _logger.LogInformation($"讀取指令集失敗：{ex}");
            }
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
                    CoinOutTimer = DateTime.Now;
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

        public void BinaryOut(params object[] p_varData)
        {
            List<byte> s_bufESCCommand = new List<byte>();
            Int32 s_intTemp;
            String s_strTemp;
            Byte[] s_bufTemp;
            String s_strTypeOf;

            foreach (object s_objTemp in p_varData)
            {
                s_strTypeOf = s_objTemp.GetType().FullName;
                switch (s_strTypeOf)
                {
                    case "System.Int16":
                        s_intTemp = (Int16)s_objTemp;
                        s_bufESCCommand.Add((byte)s_intTemp);

                        break;

                    case "System.Int32":
                        s_intTemp = (Int32)s_objTemp;
                        do
                        {
                            s_bufESCCommand.Add((byte)s_intTemp);
                            s_intTemp = s_intTemp >> 8;
                        } while (s_intTemp > 0);

                        break;

                    case "System.String":
                        s_strTemp = s_objTemp.ToString();
                        foreach (char s_chrTemp in s_strTemp.ToCharArray())
                        {
                            s_bufESCCommand.Add((byte)s_chrTemp);
                        }
                        break;

                    case "System.Byte[]":
                        s_bufTemp = (Byte[])s_objTemp;
                        foreach (byte s_chrTemp in s_bufTemp)
                        {
                            s_bufESCCommand.Add((byte)s_chrTemp);
                        }
                        break;

                    default:

                        break;
                }
            }
            WP_K837_serialPort.Write(s_bufESCCommand.ToArray(), 0, s_bufESCCommand.Count);
        }
        public void testrecipt()
        {
            _logger.LogInformation("testrecipt start");
            DateTime dateTime = DateTime.Now;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            EscPOS POSCmd = new EscPOS();
            BinaryOut(POSCmd.StatusRealTime());
            BinaryOut(POSCmd.Initialize());
            BinaryOut(POSCmd.PageMode(true));
            BinaryOut(POSCmd.Align(1));
            BinaryOut(POSCmd.PrintNV(1));
            BinaryOut(POSCmd.LineSpacing(7 * 8));
            BinaryOut(POSCmd.LineSpacing(75));

            BinaryOut(POSCmd.FontSize(1, 1), POSCmd.EmphasizedMode(false), Encoding.GetEncoding("Big5").GetBytes($"訂單號碼:000000000000"), POSCmd.CrLf());
            BinaryOut(POSCmd.PrintNV(1));
            BinaryOut(POSCmd.LineSpacing(0));

            BinaryOut(POSCmd.FontSize(0, 0), POSCmd.EmphasizedMode(false), Encoding.GetEncoding("Big5").GetBytes($"▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔"), POSCmd.CrLf());
            BinaryOut(POSCmd.PrintNV(1));
            BinaryOut(POSCmd.LineSpacing(7 * 8));

            BinaryOut(POSCmd.FontSize(1, 1), POSCmd.EmphasizedMode(false), Encoding.GetEncoding("Big5").GetBytes($"訂單編號:0000"), POSCmd.CrLf());
            BinaryOut(POSCmd.PrintNV(1));
            BinaryOut(POSCmd.PrintNV(1));
            BinaryOut(POSCmd.FontSize(1, 1), POSCmd.EmphasizedMode(false), Encoding.GetEncoding("Big5").GetBytes($"包廂:測試"), POSCmd.CrLf());
            BinaryOut(POSCmd.PrintNV(1));
            BinaryOut(POSCmd.FontSize(1, 1), POSCmd.EmphasizedMode(false), Encoding.GetEncoding("Big5").GetBytes($"購買時數:0"), POSCmd.CrLf());
            BinaryOut(POSCmd.PrintNV(1));
            BinaryOut(POSCmd.FontSize(1, 1), POSCmd.EmphasizedMode(false), Encoding.GetEncoding("Big5").GetBytes($"交易時間:{dateTime.Hour}時{dateTime.Minute}分 "), POSCmd.CrLf());
            BinaryOut(POSCmd.PrintNV(1));
            BinaryOut(POSCmd.FontSize(1, 1), POSCmd.EmphasizedMode(false), Encoding.GetEncoding("Big5").GetBytes($"結束時間: 時 分 "), POSCmd.CrLf());
            BinaryOut(POSCmd.PrintNV(1));
            //BinaryOut(POSCmd.FontSize(1, 1), POSCmd.EmphasizedMode(true), Encoding.GetEncoding("Big5").GetBytes($"購買時數{Hours}"), POSCmd.CrLf());
            BinaryOut(POSCmd.FontSize(1, 1), POSCmd.EmphasizedMode(false), Encoding.GetEncoding("Big5").GetBytes($"總計:0元"), POSCmd.CrLf());
            BinaryOut(POSCmd.PrintNV(1));
            BinaryOut(POSCmd.Align(0), POSCmd.LineSpacing(0));
            //BinaryOut(POSCmd.FontSize(0, 0), POSCmd.EmphasizedMode(false), Encoding.GetEncoding("Big5").GetBytes($"訂單編號：:{OrderId}     總計 {Amount}"), POSCmd.CrLf());
            //BinaryOut(POSCmd.FontSize(0, 0), POSCmd.EmphasizedMode(false), Encoding.GetEncoding("Big5").GetBytes("賣方12345678     買方87654321"), POSCmd.CrLf());
            BinaryOut(POSCmd.Align(1));
            BinaryOut(0x1d, 0x57, 0x87, 0x01);
            BinaryOut(POSCmd.Align(0));
            BinaryOut(POSCmd.FeedDot(120));
            BinaryOut(POSCmd.CutPartial());

        }

        public void recipt(Int64 OrderId, string TableName, int TableId,Decimal Hours,int Amount,DateTime dateTime,int HourRate)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            EscPOS POSCmd = new EscPOS();
            DateTime EndTime;
            try
            {
                 EndTime = _tablecontroller.Endtime(TableId);
            }
            catch(Exception ex)
            {
                _logger.LogError($"嘗試獲得桌號結束時間時錯誤 Ex:{ex}");
                 EndTime = DateTime.Now;
            }



            BinaryOut(POSCmd.StatusRealTime());
            BinaryOut(POSCmd.Initialize());
            BinaryOut(POSCmd.PageMode(true));
            BinaryOut(POSCmd.Align(1));
            BinaryOut(POSCmd.PrintNV(1));
            BinaryOut(POSCmd.LineSpacing(7 * 8));
            BinaryOut(POSCmd.LineSpacing(75));
            BinaryOut(POSCmd.FontSize(1, 1), POSCmd.EmphasizedMode(false), Encoding.GetEncoding("Big5").GetBytes($"訂單號碼:{OrderId.ToString().Substring(8,4)}"), POSCmd.CrLf());
            BinaryOut(POSCmd.PrintNV(1));
            BinaryOut(POSCmd.LineSpacing(0));

            BinaryOut(POSCmd.FontSize(0, 0), POSCmd.EmphasizedMode(false), Encoding.GetEncoding("Big5").GetBytes($"▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔"), POSCmd.CrLf());
            BinaryOut(POSCmd.PrintNV(1));
            BinaryOut(POSCmd.LineSpacing(7 * 8));

            BinaryOut(POSCmd.FontSize(1, 1), POSCmd.EmphasizedMode(false), Encoding.GetEncoding("Big5").GetBytes($"訂單編號:{OrderId}"), POSCmd.CrLf());
            BinaryOut(POSCmd.PrintNV(1));
            BinaryOut(POSCmd.FontSize(1, 1), POSCmd.EmphasizedMode(false), Encoding.GetEncoding("Big5").GetBytes($"日期:{dateTime.Month}月{dateTime.Day}日"), POSCmd.CrLf());
            BinaryOut(POSCmd.PrintNV(1));
            BinaryOut(POSCmd.FontSize(1, 1), POSCmd.EmphasizedMode(false), Encoding.GetEncoding("Big5").GetBytes($"費率:{HourRate}元/時"), POSCmd.CrLf());
            BinaryOut(POSCmd.PrintNV(1));
            BinaryOut(POSCmd.FontSize(1, 1), POSCmd.EmphasizedMode(false), Encoding.GetEncoding("Big5").GetBytes($"包廂:{TableName}"), POSCmd.CrLf());
            BinaryOut(POSCmd.PrintNV(1));
            BinaryOut(POSCmd.FontSize(1, 1), POSCmd.EmphasizedMode(false), Encoding.GetEncoding("Big5").GetBytes($"購買時數:{Hours}"), POSCmd.CrLf());
            BinaryOut(POSCmd.PrintNV(1));
            BinaryOut(POSCmd.FontSize(1, 1), POSCmd.EmphasizedMode(false), Encoding.GetEncoding("Big5").GetBytes($"交易時間:{dateTime.Hour}時{dateTime.Minute}分 "), POSCmd.CrLf());
            BinaryOut(POSCmd.PrintNV(1));
            BinaryOut(POSCmd.FontSize(1, 1), POSCmd.EmphasizedMode(false), Encoding.GetEncoding("Big5").GetBytes($"結束時間:{EndTime.Hour}時{EndTime.Minute}分 "), POSCmd.CrLf());
            _logger.LogInformation($"結束時間:{EndTime.Hour}時{EndTime.Minute}分 ");
            _logger.LogInformation($"包廂:{TableName}");
            _logger.LogInformation($"桌號:{TableId}");
            BinaryOut(POSCmd.PrintNV(1));
            //BinaryOut(POSCmd.FontSize(1, 1), POSCmd.EmphasizedMode(true), Encoding.GetEncoding("Big5").GetBytes($"購買時數{Hours}"), POSCmd.CrLf());
            BinaryOut(POSCmd.FontSize(1, 1), POSCmd.EmphasizedMode(false), Encoding.GetEncoding("Big5").GetBytes($"總計:{Amount}元"), POSCmd.CrLf());
            BinaryOut(POSCmd.PrintNV(1));
            BinaryOut(POSCmd.Align(0), POSCmd.LineSpacing(0));
            //BinaryOut(POSCmd.FontSize(0, 0), POSCmd.EmphasizedMode(false), Encoding.GetEncoding("Big5").GetBytes($"訂單編號：:{OrderId}     總計 {Amount}"), POSCmd.CrLf());
            //BinaryOut(POSCmd.FontSize(0, 0), POSCmd.EmphasizedMode(false), Encoding.GetEncoding("Big5").GetBytes("賣方12345678     買方87654321"), POSCmd.CrLf());
            BinaryOut(POSCmd.Align(1));
            BinaryOut(0x1d, 0x57, 0x87, 0x01);
            BinaryOut(POSCmd.Align(0));
            BinaryOut(POSCmd.FeedDot(120));
            BinaryOut(POSCmd.CutPartial());

        }

    }

    class EscPOS
    {
        public EscPOS() { }
        ~EscPOS() { }

        //public enum Alignment{ Left, Center, Right};

        public byte[] Initialize() { return new byte[2] { 0x1b, 0x40 }; }
        public byte[] PageMode(Boolean Param) { return Param ? new byte[2] { 0x1b, 0x02 } : new byte[2] { 0x1b, 0x03 }; }
        public byte[] Align(byte Param) { return new byte[3] { 0x1b, 0x61, Param }; }

        public byte[] CutPartial() { return new byte[2] { 0x1b, 0x6d }; }
        public byte[] CutFull() { return new byte[2] { 0x1b, 0x69 }; }

        public byte[] EmphasizedMode(Boolean Param) { return new byte[3] { 0x1b, 0x47, (byte)(Param ? 1 : 0) }; }
        public byte[] LineSpacing(byte Param) { return new byte[3] { 0x1b, 0x33, Param }; }
        public byte[] FeedLine(byte Param) { return new byte[3] { 0x1b, 0x64, Param }; }
        public byte[] FeedDot(byte Param) { return new byte[3] { 0x1b, 0x4a, Param }; }
        public byte[] FeedDotBack(byte Param) { return new byte[3] { 0x1b, 0x4B, Param }; }
        public byte[] CrLf() { return new byte[2] { 0x0d, 0x0a }; }
        public byte[] FontSize(byte Width, byte Height) { return new byte[3] { 0x1d, 0x21, (byte)(Width << 4 | Height) }; }

        public byte[] PrintNV(byte Param) { return new byte[4] { 0x1c, 0x70, Param, 0x0 }; }

        public byte[] StatusRealTime() { return new byte[12] { 0x10, 0x04, 1, 0x10, 0x04, 2, 0x10, 0x04, 3, 0x10, 0x04, 4 }; }

        public byte[] eReceiptBarCode(byte[] p_bufData, int p_intHeight)
        {
            byte[] s_bufHeader = new byte[] { 0x1D, 0x48, 0x30, 0x1d, 0x66, 0, 0x1d, 0x77, 1, 0x1d, 0x68, (byte)p_intHeight, 0x1d, 0x6B, 69, (byte)p_bufData.Length };
            byte[] s_bufReturn = new byte[s_bufHeader.Length + p_bufData.Length];
            s_bufHeader.CopyTo(s_bufReturn, 0);
            p_bufData.CopyTo(s_bufReturn, s_bufHeader.Length);
            return s_bufReturn;
        }

        public byte[] eReceiptQRCode(byte[] p_bufData)
        {
            byte[] s_bufHeader = new byte[] {   0x1d, 0x28, 0x6b, 4, 0, 0x31, 0x41, 50, 0,
                                                0x1d, 0x28, 0x6b, 3, 0, 0x31, 0x43, 3,
                                                0x1d, 0x28, 0x6b, 3, 0, 0x31, 0x45, 48,
                                                0x1d, 0x28, 0x6b, (byte)((p_bufData.Length +3) & 0xff), (byte)((p_bufData.Length +3)>>8), 0x31, 0x50, 0x30};
            byte[] s_bufFooter = new byte[] { 0x1d, 0x28, 0x6b, 3, 0, 0x31, 0x51, 0x30 };
            byte[] s_bufReturn = new byte[s_bufHeader.Length + p_bufData.Length + s_bufFooter.Length];
            s_bufHeader.CopyTo(s_bufReturn, 0);
            p_bufData.CopyTo(s_bufReturn, s_bufHeader.Length);
            s_bufFooter.CopyTo(s_bufReturn, s_bufHeader.Length + p_bufData.Length);
            return s_bufReturn;
        }

        public byte[] eReceiptQRCode(byte[] p_bufData, Int16 p_i16ForceVersion)
        {
            byte[] s_bufHeader = new byte[] {   0x1D, 0x28, 0x6B, 3, 0, 0x31, 0x76, (byte)p_i16ForceVersion,
                                                0x1d, 0x28, 0x6b, 4, 0, 0x31, 0x41, 50, 0,
                                                0x1d, 0x28, 0x6b, 3, 0, 0x31, 0x43, 3,
                                                0x1d, 0x28, 0x6b, 3, 0, 0x31, 0x45, 48,
                                                0x1d, 0x28, 0x6b, (byte)((p_bufData.Length +3) & 0xff), (byte)((p_bufData.Length +3)>>8), 0x31, 0x50, 0x30};
            byte[] s_bufFooter = new byte[] { 0x1d, 0x28, 0x6b, 3, 0, 0x31, 0x51, 0x30 };
            byte[] s_bufReturn = new byte[s_bufHeader.Length + p_bufData.Length + s_bufFooter.Length];
            s_bufHeader.CopyTo(s_bufReturn, 0);
            p_bufData.CopyTo(s_bufReturn, s_bufHeader.Length);
            s_bufFooter.CopyTo(s_bufReturn, s_bufHeader.Length + p_bufData.Length);
            return s_bufReturn;
        }

    }


    public class FullCommandsMap
    {
        public Dictionary<string, HardwareCommands> Hardware { get; set; }
    }

    public class HardwareCommands
    {
        public Dictionary<string, string> Commands { get; set; }
        public string Description { get; set; } // 新增 Description 屬性
    }
}
