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
using ESC_POS_USB_NET.Printer;
using ESC_POS_USB_NET.EpsonCommands;
using ESC_POS_USB_NET;
using System.Text.Unicode;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Net.Http;
using System.Reflection.Metadata;
using Serilog.Core;


namespace Checkout.Services
{
    
    public interface ISerialPortService
    {
        void OpenPort();
        void ClosePort();
        void PS100Write(string data);
        void XC100Write(string command , int amount);
        event EventHandler<string> DataReceived;
        void Checkout_1();
        void Checkout_2(int CheckoutAmount);
        void Checkout_3();
        void XC100StockUpdate(int amount, int method);
        int XC100StockScan();
        void K837Transport(string data);
        void K837Write(string data);
        void K837WriteChinese(string data);
        //void recipt();
    }

    public class SerialPortService : ISerialPortService
    {

        private HttpClient _LifePlusClient;
        public string _LifePlusSID;
        private SerialPort PS100_serialPort;
        private SerialPort XC100_serialPort;
        private SerialPort WP_K837_serialPort;
        //private SerialPort MiniHopper_serialPort;

        private readonly ILogger<SerialPortService> _logger;
        public event EventHandler<string> CheckoutCompleted;
        public event EventHandler<string> DataReceived;
        private readonly IHubContext<NotificationHub> _notificationHubContext;
        private Dictionary<string, HardwareCommands> fullCommandsMap = new Dictionary<string, HardwareCommands>();

        public SerialPortService(string portName, int baudRate, ILogger<SerialPortService> logger, IHubContext<NotificationHub> notificationHubContext)
        {
            
            //初始化XC100串口設定
            XC100_serialPort = new SerialPort("COM1");
            XC100_serialPort.BaudRate = 9600;
            XC100_serialPort.Parity = Parity.None;
            XC100_serialPort.DataBits = 8;
            XC100_serialPort.StopBits = StopBits.One;
            XC100_serialPort.DataReceived += XC100_serialPort_DataReceived;

            //初始化WP-K837崁入式印表機串口設定
            WP_K837_serialPort = new SerialPort("COM11");
            WP_K837_serialPort.BaudRate = 9600;
            //WP_K837_serialPort.Parity = Parity.None;
            //WP_K837_serialPort.DataBits = 8;
            //WP_K837_serialPort.StopBits = StopBits.One;
            //初始化Mini Hopper串口設定


            //初始化PS100設定
            PS100_serialPort = new SerialPort(portName, baudRate);
            PS100_serialPort.DataReceived += SerialPortDataReceived;

            //初始化服務設定
            try
            {
                OpenPort();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }            
            _logger = logger;//注入log
            _notificationHubContext = notificationHubContext; // 注入通知上下文

            //初始化指令描述
            string jsonFilePath = "config/command.json"; // 請替換成 command.json 的實際路徑
            string jsonContent = File.ReadAllText(jsonFilePath);
            JObject commandData = JObject.Parse(jsonContent);
            LoadCommandsFromJson(jsonFilePath);

            Console.WriteLine("Service here");

            _LifePlusClient = new HttpClient();
            _LifePlusClient.BaseAddress = new Uri("https://bossnet-apis-test.lifeplus.tw/");

            LifePlusLogin();

        }


        public void OpenPort()
        {
            PS100_serialPort.Open();
            //XC100_serialPort.Open();
            WP_K837_serialPort.Open();
        }

        public void ClosePort()
        {
            PS100_serialPort.Close();
            //XC100_serialPort.Close();
            WP_K837_serialPort.Close();
        }

        public void PrinterCut(bool full)
        {
            if (full)
            {
                K837Transport("1D5600");
            }
            else
            {
                K837Transport("1D5611");
            }
        }
        public void K837WriteChinese(string data)
        {
            Encoding utf8 = Encoding.UTF8;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var big5 = Encoding.GetEncoding(950);
            byte[] utf8Bytes = utf8.GetBytes(data + "\n");
            byte[] big5Bytes = Encoding.Convert(utf8, big5, utf8Bytes);
            //Encoding big5 = Encoding.GetEncoding(1252);
            //byte[] big5Bytes = big5.GetBytes(data);
            if (WP_K837_serialPort.IsOpen)
            {
                _logger.LogInformation($"writed to WP_K837_serialPort: {data}");
                //byte[] dataToSend = StringToByteArray(data);

                try
                {
                    WP_K837_serialPort.Write(big5Bytes, 0, big5Bytes.Length);
                    //PS100_serialPort.Write(dataToSend, 0, dataToSend.Length);
                    //_logger.LogInformation($"K837 寫入: {BitConverter.ToString(dataToSend)}");
                }
                catch
                {
                    _logger.LogInformation("WP_K837_serialPort failed to write");
                }
            }
            else
            {
                // 可以在串口未打开时进行适当的错误处理或记录日志
                // 例如，记录日志并抛出异常，或者发送通知等
                _logger.LogError("串口未打开，无法发送数据");
            }
        }
        public void K837Transport(string data)
        {
            LifePlusEnvoice(_LifePlusSID,200);

            if (WP_K837_serialPort.IsOpen)
            {
                byte[] dataToSend = StringToByteArray(data);
                _logger.LogInformation($"writed to WP_K837_serialPort: {dataToSend}");

                try
                { 
                    WP_K837_serialPort.Write(dataToSend, 0, dataToSend.Length);
                    //PS100_serialPort.Write(dataToSend, 0, dataToSend.Length);
                    //_logger.LogInformation($"K837 寫入: {BitConverter.ToString(dataToSend)}");
                }
                catch
                {
                    _logger.LogInformation("WP_K837_serialPort failed to write");
                }
            }
            else
            {
                // 可以在串口未打开时进行适当的错误处理或记录日志
                // 例如，记录日志并抛出异常，或者发送通知等
                _logger.LogError("串口未打开，无法发送数据");
            }
        }
        public class order
        {
            public string ItemName { get; set; }
            public int ItemAmount { get; set; }
            public int OrderTotal { get; set; }
        }
        public class LoginApiResponse
        {
            public string retVal { get; set; }
            public int retCode { get; set; }
            public List<object> settings { get; set; }
        }

        public class EInvoiceData
        {
            public string Invoice_Number { get; set; }
            public string RandomNumer { get; set; }
            public string QRCode { get; set; }
            public string PrintMark { get; set; }
            public string CarrierType1 { get; set; }
            public string CarrierType2 { get; set; }
            public string NPOBAN { get; set; }
        }

        public class LifePlusEnvoiceResponse
        {
            public class EnvoiceRetval
            {
                public string shop_Name { get; set; }
                public string seller_Name { get; set; }
                public string seller_Identifier { get; set; }
                public string CarrierType { get; set; }
                public string seller_Address { get; set; }
                public List<EInvoiceData> einv_datas { get; set; }
            }

            public EnvoiceRetval retVal { get; set; }
            public int retCode { get; set; }
        }
        public class EInvoiceInfo
        {
            public string LeftQRCode { get; set; }
            public string RightQRCode { get; set; }
        }

        public async Task<string> LifePlusLogin()
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("userid", "tm_100577@lifeplus.tw"),
                new KeyValuePair<string, string>("passwd", "!tm_100577@#"),
                new KeyValuePair<string, string>("TM_Location_ID", "100577"),
                new KeyValuePair<string, string>("dev_id", "3201611163"),
                new KeyValuePair<string, string>("edc_id", "ED094110"),
                new KeyValuePair<string, string>("seller_identifier", "23475909"),
                new KeyValuePair<string, string>("ver", "02")
            });

            var response = await _LifePlusClient.PostAsync("api/adminV2/apiLogin", content);

            if (response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                LoginApiResponse apiResponse = JsonConvert.DeserializeObject<List<LoginApiResponse>>(responseBody).FirstOrDefault();
                if (apiResponse != null)
                {
                    _LifePlusSID = apiResponse.retVal;
                    _logger.LogInformation($"SID:{_LifePlusSID}");
                    return _LifePlusSID;
                }
            }

            return null; // 或者根据失败情况返回其他适当的值
        }
        public async Task<string> LifePlusEnvoice(string sid,long n_TXN_Amount)
        {
            _logger.LogInformation($"tryin to get envoice");
            var data = new FormUrlEncodedContent(new[]
            {

                new KeyValuePair<string, string>("TM_Location_ID", "100577"),
                new KeyValuePair<string, string>("n_TXN_Date_Time", DateTime.Now.ToString()),
                new KeyValuePair<string, string>("einv_ym", "202312"),
                new KeyValuePair<string, string>("n_Count", "1"),
                new KeyValuePair<string, string>("utf8", "1")
            });
            _logger.LogInformation($"tryin to get envoice");

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("sid", sid),
                new KeyValuePair<string, string>("data", "[{\r\n \"TM_Location_ID\": \"100577\",\r\n \"n_TXN_Date_Time\": \"20231103132433\",\r\n \"einv_ym\": \"202312\",\r\n \"n_Count\": 1,\r\n \"utf8\": 1\r\n}]")
            });
            var response = await _LifePlusClient.PostAsync("api/einvoiceV2/txnqueryv08", content);
            if (response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                _logger.LogInformation($"{responseBody}");
                LifePlusEnvoiceResponse envoiceResponse = JsonConvert.DeserializeObject<List<LifePlusEnvoiceResponse>>(responseBody).FirstOrDefault();
                _logger.LogInformation($"1");

                //EInvoiceData eInvoiceData = JsonConvert.DeserializeObject<List<EInvoiceData>>(envoiceResponse.ToString()).FirstOrDefault();
                //_logger.LogInformation($"2");

                if (envoiceResponse != null)
                {
                    _logger.LogInformation($"2");

                    string d12d = envoiceResponse.retVal.seller_Name;
                    _logger.LogInformation($"{d12d}");
                    foreach ( var einvoiceData in envoiceResponse.retVal.einv_datas)
                    {
                        string invoiceNumber = einvoiceData.Invoice_Number;
                        string randomNumer = einvoiceData.RandomNumer;
                        string qrCode = einvoiceData.QRCode;
                        string printMark = einvoiceData.PrintMark;
                        string carrierType1 = einvoiceData.CarrierType1;
                        string carrierType2 = einvoiceData.CarrierType2;
                        string npoban = einvoiceData.NPOBAN;
                        recipt(invoiceNumber, randomNumer, qrCode,"",n_TXN_Amount);
                        // 根据数据的具体需求执行逻辑或日志记录
                    }


                    return envoiceResponse.ToString();
                }

            }
            return null;
            _logger.LogInformation($"");
        }
        public void recipt(string invoiceNumber, string randomNumer, string qrCode,string buyer_identifier,long n_TXN_Amount)
        {
            // 取得今天日期
            DateTime today = DateTime.Today;
            string time = DateTime.Now.TimeOfDay.ToString().Substring(0,8);
            // 轉換為民國年，取得年份後扣掉1911
            string YearTW = (today.Year - 1911).ToString();
            // 將日期格式為YYYMMDD
            string DateTW = YearTW + today.ToString("MMdd");
            // 將字串補齊
            //DateTW = DateTW.PadRight(7, '0');
            //發票期別
            int Period = (DateTime.Now.Month+1)/2 * 2;

            string PeriodTitle =$"{YearTW}年{(Period-1).ToString().PadLeft(2, '0')}-{Period.ToString().PadLeft(2, '0')}月";
            string PeriodCode = $"{YearTW}{Period.ToString().PadLeft(2,'0')}";

            _logger.LogInformation(PeriodTitle);
            _logger.LogInformation(PeriodCode);

            //總計額(8碼)
            string AmountString = n_TXN_Amount.ToString().PadLeft(8,'0');
            //買方統編(8碼)
            buyer_identifier = buyer_identifier.PadLeft(8, '0');
            // 构建左侧二维码的内容
            String leftQRContent = $"{invoiceNumber}{DateTW}{randomNumer}00000000{AmountString}{buyer_identifier}23475909{qrCode}:**********:1:0:1";
            String rightQRContent = $"**:包台費:1:{n_TXN_Amount}";
            //_logger.LogInformation(leftQRContent);
            string barCode = $"{PeriodCode}{invoiceNumber}{randomNumer}";
            //_logger.LogInformation(barCode);
            int maxAllowedLength = 1;
            String s_str1stQRCode = "AB112233441020523999900000145000001540987654312345678ydXZt4LAN1UHN/j1juVcRA==:**********:2:0:2:乾電池:1:105:口罩:1:210:牛奶:1:25";
            String s_str2ndQRCode = "**:WPT810熱感式印表機:1:???:二維條碼若已記載完整明細資訊後，營業人可在此自行增加其他資訊";
            // 如果左侧内容超出长度，需要分割填入右侧二维码
            
            
            
            //BinaryOut(512, (Int16)123, "222", 0x1B, Encoding.UTF8.GetBytes("大笨蛋"), Encoding.GetEncoding("Big5").GetBytes("大笨蛋"));

            //String s_str1stQRCode = "AB112233441020523999900000145000001540987654312345678ydXZt4LAN1UHN/j1juVcRA==:**********:3:3:1:乾電池:1:105:口罩:1:210:牛奶:1:25";
            //String s_str2ndQRCode = "**:WPT810熱感式印表機:1:???:二維條碼若已記載完整明細資訊後，營業人可在此自行增加其他資訊";
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            EscPOS POSCmd = new EscPOS();

            //Boolean s_bolAutoConnect = !ComPort.IsOpen;

            //if (!OpenService(true)) { return; }

            BinaryOut(POSCmd.StatusRealTime());

            BinaryOut(POSCmd.Initialize());
            BinaryOut(POSCmd.PageMode(true));

            BinaryOut(POSCmd.Align(1));
            BinaryOut(POSCmd.PrintNV(1));
            BinaryOut(POSCmd.LineSpacing(7 * 8));

            BinaryOut(POSCmd.FontSize(1, 1), POSCmd.EmphasizedMode(false), Encoding.GetEncoding("Big5").GetBytes("電子發票證明聯"), POSCmd.CrLf());
            BinaryOut(POSCmd.FontSize(1, 1), POSCmd.EmphasizedMode(true), Encoding.GetEncoding("Big5").GetBytes($"{PeriodTitle}"), POSCmd.CrLf());
            BinaryOut(POSCmd.FontSize(1, 1), POSCmd.EmphasizedMode(true), Encoding.GetEncoding("Big5").GetBytes($"{invoiceNumber.Substring(0,2)}-{invoiceNumber.Substring(2)}"), POSCmd.CrLf());

            BinaryOut(POSCmd.Align(0), POSCmd.LineSpacing(0));

            BinaryOut(POSCmd.FontSize(0, 0), POSCmd.EmphasizedMode(false), Encoding.GetEncoding("Big5").GetBytes($"{today.Year}-{today.Month}-{today.Day}  {time}"), POSCmd.CrLf());
            BinaryOut(POSCmd.FontSize(0, 0), POSCmd.EmphasizedMode(false), Encoding.GetEncoding("Big5").GetBytes($"隨機碼：{randomNumer}     總計：{n_TXN_Amount}"), POSCmd.CrLf());
            BinaryOut(POSCmd.FontSize(0, 0), POSCmd.EmphasizedMode(false), Encoding.GetEncoding("Big5").GetBytes($"賣方12345678"), POSCmd.CrLf());
            //BinaryOut(POSCmd.FontSize(0, 0), POSCmd.EmphasizedMode(false), Encoding.GetEncoding("Big5").GetBytes($"賣方12345678     買方{buyer_identifier}"), POSCmd.CrLf());

            BinaryOut(POSCmd.Align(1));
            BinaryOut(0x1d, 0x57, 0x87, 0x01);

            BinaryOut(POSCmd.FeedDot(16));
            BinaryOut(POSCmd.eReceiptBarCode(Encoding.GetEncoding("Big5").GetBytes(barCode), 64));
            BinaryOut(POSCmd.FeedDot(16));

            BinaryOut(POSCmd.Align(0));

            BinaryOut(0x1b, "$", 40, 0, POSCmd.eReceiptQRCode(Encoding.UTF8.GetBytes(leftQRContent),6));

            BinaryOut(POSCmd.FeedDotBack(129));
            //BinaryOut(POSCmd.FeedDotBack(172));

            BinaryOut(0x1b, "$", 226, 0, POSCmd.eReceiptQRCode(Encoding.UTF8.GetBytes(rightQRContent),6));

            BinaryOut(POSCmd.FeedDot(120), POSCmd.CutPartial());
            //if (s_bolAutoConnect) OpenService(false);

        }

       
        public void K837Write(string data)
        {
            if (WP_K837_serialPort.IsOpen)
            {
                _logger.LogInformation($"writed to WP_K837_serialPort: {data}");
                //byte[] dataToSend = StringToByteArray(data);
                byte[] datatosend = Encoding.ASCII.GetBytes(data);

                try
                {
                    WP_K837_serialPort.Write(datatosend, 0, datatosend.Length);
                    //PS100_serialPort.Write(dataToSend, 0, dataToSend.Length);
                    //_logger.LogInformation($"K837 寫入: {BitConverter.ToString(dataToSend)}");
                }
                catch
                {
                    _logger.LogInformation("WP_K837_serialPort failed to write");
                }
            }
            else
            {
                // 可以在串口未打开时进行适当的错误处理或记录日志
                // 例如，记录日志并抛出异常，或者发送通知等
                _logger.LogError("串口未打开，无法发送数据");
            }
        }

        public void XC100Write(string command , int amount)
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
            //退款金額歸零
            PayoutValue = 0;
            //已投入金額歸零
            ReceivedAmount = 0;         //紙鈔計數歸零
            CoinReceivedAmount = 0;     //硬幣計數歸零
            //投錢狀態歸零
            CoinAmount = 0;
            PaperAmount = 0;
            TotalAmount = 0;
            //開啟現金設備
            PS100Write(GenerateFullCommand("03", 0, 0));      //開啟投幣器
            System.Threading.Thread.Sleep(100);
            PS100Write(GenerateFullCommand("04", 0, 0));      //開啟紙鈔機
        }
        int PayoutValue = 0;
        //結帳按鈕
        public void Checkout_2(int CheckoutAmount)
        {            
            //設定出鈔的設備 1:PS100 2:XC100
            int PaperMode = 2;
            //計算找零金額
            int PayoutAmount = ReceivedAmount + CoinReceivedAmount - CheckoutAmount;
            PayoutValue = PayoutAmount;
            _logger.LogInformation($"PayoutAmount:{PayoutAmount},ReceivedAmount:{ReceivedAmount} ,CoinReceivedAmount:{CoinReceivedAmount} ,CheckoutAmount:{CheckoutAmount}");
            //驗證是否已完成找零 -- 若 找零狀態 == 找零金額 就傳送 "結帳完成"
            if ( TotalAmount == PayoutValue)
            {
                OnCheckoutCompleted(this, "結帳完成");
            }
            //出鈔 
            int PayoutPaper = (PayoutAmount / 100);
            int PayoutCoin = 0;
            if (PayoutPaper > 0)
            {
                _logger.LogInformation($"找鈔 {PayoutPaper*100}元");
                if (PaperMode == 1)       //--使用PS100出鈔
                {
                    PS100Write(GenerateFullCommand("10", PayoutPaper*100, 0));
                    System.Threading.Thread.Sleep(1000);
                }
                else if(PaperMode == 2)   //--使用XC100出鈔
                {
                    if (PayoutPaper > XC100Stock)
                    {
                        PayoutCoin += (PayoutPaper - XC100Stock) * 100;
                        PayoutPaper = XC100Stock;
                        _logger.LogInformation("鈔票儲存量不足找零金額");
                    }
                    XC100Write("B", PayoutPaper);
                }
            }
            //出幣
            PayoutCoin += PayoutAmount % 100;
            if (PayoutCoin > 0)
            {
                PS100Write(GenerateFullCommand("11", PayoutCoin, 0));
                _logger.LogInformation($"找零錢 {PayoutCoin}元");
                System.Threading.Thread.Sleep(1000);
            }
            PS100Write(GenerateFullCommand("05", 0, 0));      //關閉投幣器
            System.Threading.Thread.Sleep(250);
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
            _logger.LogInformation($"ReceivedAmount:{ReceivedAmount} ,CoinReceivedAmount:{CoinReceivedAmount}");
            
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
                _logger.LogInformation($"writed to PS100 serial port: {data}");
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

        private string XC100buffer;
        private void XC100_serialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            byte[] dataBytes = ReadDataAsBytes(XC100_serialPort);
            string data = string.Join(" ", dataBytes.Select(b => b.ToString("X2"))); // 使用空格分隔并转为十六进制字符串
            XC100buffer += data+" ";
            if (XC100buffer.Contains("02") && XC100buffer.Contains("03"))
            {
                _logger.LogInformation($"XC100 回傳: {XC100buffer}");
                string commandByte = XC100buffer.Substring(9,2);
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
                else if(dataByte2 != "0")
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
                        XC100Stock -= Int32.Parse(dataString) ;
                        _logger.LogInformation($"XC100 剩餘: {XC100Stock}張");
                        break;
                }
                XC100buffer = "";
            }
            else if(XC100buffer.Contains("06"))
            {
                _logger.LogInformation($"XC100 回傳: {XC100buffer}");
                XC100buffer = "";
            }
        }

        private string receiveBuffer = "";
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
                    response(commandByte);

                    //判斷指令類型
                    switch (commandByte)
                    {
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
                            break ;
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
                    
                    OnCheckoutCompleted(this,"結帳完成");
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

        private void response(string data)
        {
            string selectedHardware = "PS100 English";
            string shortCommand = "0x" + data;

            if (fullCommandsMap.ContainsKey(selectedHardware) &&
                fullCommandsMap[selectedHardware].Commands.ContainsKey(shortCommand))
            {
                string description = fullCommandsMap[selectedHardware].Commands[shortCommand];
                string formattedData = $"[{shortCommand}]: {description} "  ;
                _logger.LogInformation(formattedData);
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

        private void ReadPaper(string data)
        {
            int catche100 = Convert.ToInt32(data.Substring(12, 2), 16);
            int catche500 = Convert.ToInt32(data.Substring(20, 2), 16);
            int oneHundred = Convert.ToInt32(data.Substring(52, 2), 16);
            int fiveHundred = Convert.ToInt32(data.Substring(60, 2), 16);
            int oneThousand = Convert.ToInt32(data.Substring(64, 2), 16);
            string formattedData = $"目前紙鈔暫存器 目前元  一百元:{catche100} 五百元:{catche500}" + "\r\n" + $"               目前紙鈔箱 目前元  一百元:{oneHundred} 五百元:{fiveHundred} 一千元:{oneThousand}";
            _logger.LogInformation(formattedData);
        }

        private void UpdateCoinData(string data)
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
                CoinReceivedAmount = sum;
                // 在 UI 執行緒上更新 richTextBox2
                _logger.LogInformation(formattedData);
            }
        }
        int BeforeAmount;
        int AfterAmount;
        bool before = false, after=false;
        private void UpdatePaperData(string data) //0x22收鈔狀態
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
                int catche100 = Convert.ToInt32(data.Substring(12, 2), 16);
                int catche500 = Convert.ToInt32(data.Substring(16, 2), 16);
                int oneHundred = Convert.ToInt32(data.Substring(32, 2), 16);
                int fiveHundred = Convert.ToInt32(data.Substring(36, 2), 16);
                int oneThousand = Convert.ToInt32(data.Substring(38, 2), 16);
                if (StateByte == "01")
                {
                    AfterAmount = oneHundred * 100 + fiveHundred*500+ oneThousand*1000 + catche100*100 + catche500*500;
                    string formattedData = $"[{State}]目前紙鈔暫存器          一百元:{catche100} 五百元:{catche500}" + "\r\n" + $"                       目前紙鈔箱 目前{AfterAmount}元  一百元:{oneHundred} 五百元:{fiveHundred} 一千元:{oneThousand}";
                    _logger.LogInformation(formattedData);
                    if (before = true)    //驗證使否卡鈔
                    {
                        after = true;
                    }
                }
                else if(StateByte == "02")
                {
                    BeforeAmount = oneHundred * 100 + fiveHundred * 500 + oneThousand * 1000 +catche100 * 100 + catche500 * 500;
                    string formattedData = $"[{State}]目前紙鈔暫存器         一百元:{catche100} 五百元:{catche500}" + "\r\n" + $"                       目前紙鈔箱 目前{BeforeAmount}元  一百元:{oneHundred} 五百元:{fiveHundred} 一千元:{oneThousand}";
                    _logger.LogInformation(formattedData);
                    before = true;
                }
                if (before && after)
                {
                    ReceivedAmount += AfterAmount - BeforeAmount;
                    _logger.LogInformation($"        收款金額:{ReceivedAmount} 收款前:{BeforeAmount} 收款後:{AfterAmount}" + "\r\n");
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
        private void CoinCount(string data) //0x22收幣狀態
        {
            CoinAmount = ((Convert.ToInt32(data.Substring(20, 2), 16)) + (Convert.ToInt32(data.Substring(22, 2), 16) * 256) + (Convert.ToInt32(data.Substring(24, 2), 16) * 256 * 256) + (Convert.ToInt32(data.Substring(26, 2), 16) * 256 * 256 * 256)) / 100;
            int Amount = ((Convert.ToInt32(data.Substring(12, 2), 16)) + (Convert.ToInt32(data.Substring(14, 2), 16) * 256) + (Convert.ToInt32(data.Substring(16, 2), 16) * 256 * 256)) / 100;
            TotalAmount = PaperAmount + CoinAmount;            
            _logger.LogInformation($"PS100 出幣:[Coin]     {Amount}元一枚  目前累計找錢金額:{TotalAmount}元" + "\r\n");
            //驗證是否已完成找零 -- 若 找零狀態 == 找零金額 就傳送 "結帳完成"
            if (TotalAmount == PayoutValue)
            {
                OnCheckoutCompleted(this, "結帳完成");
            }
        }
        private void PaperCount(string data)
        {
            PaperAmount = ((Convert.ToInt32(data.Substring(20, 2), 16)) + (Convert.ToInt32(data.Substring(22, 2), 16) * 256) + (Convert.ToInt32(data.Substring(24, 2), 16) * 256 * 256) + (Convert.ToInt32(data.Substring(26, 2), 16) *256 * 256 * 256)) / 100;
            int Amount =((Convert.ToInt32(data.Substring(12, 2), 16)) + (Convert.ToInt32(data.Substring(14, 2), 16)*256) + (Convert.ToInt32(data.Substring(16, 2), 16) *256 * 256)) / 100;
            TotalAmount = PaperAmount + CoinAmount;
            _logger.LogInformation($"PS100 出鈔:[Paper]    {Amount}元一張  目前累計找錢金額:{TotalAmount}元" + "\r\n");
            //驗證是否已完成找零 -- 若 找零狀態 == 找零金額 就傳送 "結帳完成"
            if (TotalAmount == PayoutValue)
            {
                OnCheckoutCompleted(this, "結帳完成");
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
