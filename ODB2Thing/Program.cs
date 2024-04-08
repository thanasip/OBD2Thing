using OBD.NET.Commands;
using OBD.NET.Communication;
using OBD.NET.Devices;
using OBD.NET.Extensions;
using OBD.NET.OBDData;
using System.IO.Ports;
using System.Text.RegularExpressions;

namespace ODB2Thing
{
    internal class Program
    {
        private static readonly List<int> _supportedPids = [];
        private static readonly Dictionary<byte, Type> _pidTypes = [];

        static async Task Main()
        {
            typeof(IOBDData).Assembly
                .GetTypes()
                .Where(t => t?.BaseType == typeof(AbstractOBDData) && !t.IsAbstract)
                .ToList()
                .ForEach(t =>
                {
                    if (Activator.CreateInstance(t) is IOBDData inst)
                        _pidTypes.TryAdd(inst.PID, t);
                });

            Console.WriteLine("Enter COM port number: ");
            var comNum = Console.ReadLine();
            if (!uint.TryParse(comNum, out var comInt))
                throw new Exception("Invalid COM port");

            using var conn = new SerialConnection($"COM{comInt}", 115200, Parity.None, StopBits.One, Handshake.None, 2000);
            using var obd2 = new ELM327(conn);

            SerialConnection.GetAvailablePorts().ToList().ForEach(s => Console.WriteLine($"Available COM port: {s}"));

            obd2.SubscribeDataReceived<IOBDData>((sender, response)
                => Console.WriteLine($"RECEIVED DATA: {response.Data} (PID: {response.Data.PID})"));

            conn.DataReceived += (data, args) =>
            {
                var asHex = string.Join(' ', args?.Data?.Where(b => b != 0)?.Select(b => $"{b:X2}") ?? []);
                //var asStr = Regex.Escape(string.Join("", args?.Data?.Where(b => b is not 0)?.Select(b => (char)b) ?? []));
                Console.WriteLine($"BYTE: {asHex}");
                //Console.WriteLine($"TEXT: {asStr}");
            };

            await obd2.InitializeAsync();
            await Task.Delay(7000);
            await GetSupportedPids(obd2);
            string? command = string.Empty;
            while (GetInput(ref command) && command is not null)
            {
                if (command.Equals("q", StringComparison.CurrentCultureIgnoreCase))
                {
                    break;
                }
                else if (command.Equals("pids", StringComparison.CurrentCultureIgnoreCase))
                {
                    foreach (var pid in _pidTypes)
                    {
                        if (_supportedPids.Contains(pid.Key))
                            Console.WriteLine($"{pid.Value.Name}: ({pid.Key})");
                    }
                }
                else
                {
                    var atCommand = ResolveCommand(command);

                    if (atCommand != null)
                    {
                        obd2.SendCommand(atCommand);
                    }
                    else
                    {
                        if (byte.TryParse(command, out var pid))
                        {
                            if (_supportedPids.Contains(pid))
                                await RequestData(pid, obd2);
                        }
                    }
                }
            }

            Console.ReadLine();
        }

        static bool GetInput(ref string? command)
        {
            Console.Write("ready > ");
            return !string.IsNullOrWhiteSpace(command = Console.ReadLine());
        }

        static async Task GetSupportedPids(ELM327 obd2)
        {
            obd2.SubscribeDataReceived<PidsSupported01_20>((sender, response) => _supportedPids.AddRange(response.Data.SupportedPids));

            await RequestData<PidsSupported01_20>(obd2);

            obd2.UnsubscribeDataReceived<PidsSupported01_20>((sender, response) => { });
        }

        static async Task RequestData<T>(ELM327 obd2) where T : class, IOBDData, new()
        {
            Console.WriteLine($"Requesting data for PID: {typeof(T).Name} ({new T().PID})");
            await obd2.RequestDataAsync<T>();
            await obd2.WaitQueueAsync();
        }

        static async Task RequestData(byte pid, ELM327 obd2)
        {
            var type = _pidTypes[pid];
            Console.WriteLine($"Requesting data for PID: {type.Name} ({pid})");
            await obd2.RequestDataAsync(type);
            await obd2.WaitQueueAsync();
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
