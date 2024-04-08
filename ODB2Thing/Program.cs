using OBD.NET.Commands;
using OBD.NET.Communication;
using OBD.NET.Devices;
using OBD.NET.Extensions;
using OBD.NET.OBDData;
using System.IO.Ports;

namespace ODB2Thing
{
    internal class Program
    {
        private static int[] _bank1Pids = [];
        private static int[] _bank2Pids = [];
        private static int[] _bank3Pids = [];

        static async Task Main()
        {
            using var conn = new SerialConnection("COM4", 115200, Parity.None, StopBits.One, Handshake.None, 2000);
            using var obd2 = new ELM327(conn);

            obd2.SubscribeDataReceived<IOBDData>((sender, response)
                => Console.WriteLine($"RECEIVED: PID {response.Data.PID.ToHexString()}: {response.Data}"));

            await obd2.InitializeAsync();
            await GetSupportedPids(obd2);
            string? command = string.Empty;

            while (!string.IsNullOrWhiteSpace(command = Console.ReadLine()))
            {
                if (!command.Equals("q", StringComparison.CurrentCultureIgnoreCase))
                {
                    var atCommand = ResolveCommand(command);

                    if (atCommand != null)
                        obd2.SendCommand(atCommand);
                }
            }

            Console.ReadLine();
        }

        static async Task GetSupportedPids(ELM327 obd2)
        {
            obd2.SubscribeDataReceived<PidsSupported01_20>((sender, response) => _bank1Pids = response.Data.SupportedPids);
            obd2.SubscribeDataReceived<PidsSupported21_40>((sender, response) => _bank2Pids = response.Data.SupportedPids);
            obd2.SubscribeDataReceived<PidsSupported41_60>((sender, response) => _bank3Pids = response.Data.SupportedPids);

            await RequestData<PidsSupported01_20>(obd2);
            await RequestData<PidsSupported21_40>(obd2);
            await RequestData<PidsSupported41_60>(obd2);

            obd2.UnsubscribeDataReceived<PidsSupported01_20>((sender, response) => _bank1Pids = response.Data.SupportedPids);
            obd2.UnsubscribeDataReceived<PidsSupported21_40>((sender, response) => _bank2Pids = response.Data.SupportedPids);
            obd2.UnsubscribeDataReceived<PidsSupported41_60>((sender, response) => _bank3Pids = response.Data.SupportedPids);
        }

        static async Task RequestData<T>(ELM327 obd2) where T : class, IOBDData, new()
        {
            Console.WriteLine($"Requesting data for PID: {typeof(T).Name} ({new T().PID})");
            await obd2.RequestDataAsync<T>();
            await Task.Delay(1000);
        }

        static ATCommand? ResolveCommand(string command) => command switch
        {
            "ATZ"   => ATCommand.ResetDevice,
            "ATRV"  => ATCommand.ReadVoltage,
            "ATE1"  => ATCommand.EchoOn,
            "ATE0"  => ATCommand.EchoOff,
            "ATH1"  => ATCommand.HeadersOn,
            "ATH0"  => ATCommand.HeadersOff,
            "ATS1"  => ATCommand.PrintSpacesOn,
            "ATS0"  => ATCommand.PrintSpacesOff,
            "ATL1"  => ATCommand.LinefeedsOn,
            "ATL0"  => ATCommand.LinefeedsOff,
            "ATSP0" => ATCommand.SetProtocolAuto,
            "ATI"   => ATCommand.PrintVersion,
            "ATPC"  => ATCommand.CloseProtocol,
            _       => null

        };
    }
}
