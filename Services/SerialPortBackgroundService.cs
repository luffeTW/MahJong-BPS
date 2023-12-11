using System;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MahJongBPS.Services
{
    public class SerialPortBackgroundService : BackgroundService
    {
        private readonly ILogger<SerialPortBackgroundService> _logger;
        private SerialPort _serialPort;

        public SerialPortBackgroundService(ILogger<SerialPortBackgroundService> logger)
        {
            _logger = logger;
            _serialPort = new SerialPort("COM4", 9600);
            _serialPort.Open();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_serialPort.BytesToRead > 0)
                {
                    byte[] dataBytes = ReadDataAsBytes(_serialPort);
                    string data = Encoding.GetEncoding("ISO-8859-1").GetString(dataBytes);
                    _logger.LogInformation("Received data from serial port: " + data);
                    // 在這裡可以對數據進行進一步處理
                }

                await Task.Delay(100, stoppingToken); // 100 毫秒的延遲
            }
        }

        private byte[] ReadDataAsBytes(SerialPort port)
        {
            byte[] buffer = new byte[port.BytesToRead];
            port.Read(buffer, 0, buffer.Length);
            return buffer;
        }

        public override void Dispose()
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
                _serialPort.Dispose();
            }

            base.Dispose();
        }
    }
}
