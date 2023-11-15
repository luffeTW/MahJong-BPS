using EzioDll;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Checkout.Controllers
{

    [Route("api/[controller]")]
    [ApiController]
    public class GoDexController : Controller
    {
        private readonly ILogger<GoDexController> _logger;

        GodexPrinter Printer = new GodexPrinter();

        //GoDex Parameter
        private string GoDEX_AdressIP = "192.168.0.158";
        private string GoDEX_Port = "9100";
        private int Cbo_PaperType = 0;
        private int Num_Label_H = 40;
        private int Num_GapFeed = 3;
        private int Num_Label_W = 54;
        private int Num_Dark = 10;
        private int Num_Speed = 3;
        private int Num_Page = 1;
        private int Num_Copy = 1;

        public GoDexController(ILogger<GoDexController> logger)
        {
            _logger = logger;

        }

        [HttpPost("GoDexPrint")]
        public IActionResult GoDexPrint()
        {
            int PosX = 100;
            int PosY = 85;
            int FontHeight = 34;

            ConnectPrinter();
            LabelSetup();
            // Print Text
            Printer.Command.Start();
            Printer.Command.PrintText_Unicode(PosX, PosY += 40, FontHeight, "Arial", "這");
            Printer.Command.PrintText_Unicode(PosX, PosY += 40, FontHeight, "Arial", "這是");
            Printer.Command.PrintText_Unicode(PosX, PosY += 40, FontHeight, "Arial", "這是中");
            Printer.Command.PrintText_Unicode(PosX, PosY += 40, FontHeight, "Arial", "這是中文");
            Printer.Command.PrintText_Unicode(PosX, PosY += 40, FontHeight, "Arial", "這是中文測試");
            Printer.Command.End();
            DisconnectPrinter();


            return Json(new { message = "操作成功" });
        }
        //GoDex Command Functions
        private void ConnectPrinter()
        {
            Printer.Open(GoDEX_AdressIP, int.Parse(GoDEX_Port));
        }

        private void DisconnectPrinter()
        {
            Printer.Close();
        }
        private void LabelSetup()
        {
            Printer.Config.LabelMode((PaperMode)Cbo_PaperType, (int)Num_Label_H, (int)Num_GapFeed);
            Printer.Config.LabelWidth((int)Num_Label_W);
            Printer.Config.Dark((int)Num_Dark);
            Printer.Config.Speed((int)Num_Speed);
            Printer.Config.PageNo((int)Num_Page);
            Printer.Config.CopyNo((int)Num_Copy);
        }
    }
}
