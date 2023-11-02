


namespace MahJongBPS
{
    using Microsoft.AspNetCore.SignalR;
    using System.Threading.Tasks;
    public class NotificationHub : Hub
    {
        public async Task SendRecviedCashNotification(int RecviedAmount)
        {
            await Clients.All.SendAsync("RecviedCash", RecviedAmount);
        }
        public async Task SendCashedNotification(int CashedAmount)
        {
            await Clients.All.SendAsync("PayoutCash", CashedAmount);
        }
        public async Task SendCheckoutNotification()
        {
            await Clients.All.SendAsync("CheckoutCompleted", "結帳已完成"); // "CheckoutCompleted" 是事件名稱，可以自行定義
        }
       
    }
}
