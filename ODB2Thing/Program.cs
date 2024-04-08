using Microsoft.CodeAnalysis.CSharp;
using OBD.NET.Commands;
using OBD.NET.Communication;
using OBD.NET.Devices;
using OBD.NET.OBDData;
using System.IO.Ports;

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

            SerialConnection.GetAvailablePorts()
                .ToList()
                .ForEach(s => Console.WriteLine($"Available COM port: {s}"));

            Console.Write("Enter COM port: ");
            var com = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(com))
                throw new Exception("Invalid COM port");

            using var conn = new SerialConnection(com.ToUpper(), 115200, Parity.None, StopBits.One, Handshake.None, 2000);
            using var obd2 = new ELM327(conn);

            obd2.SubscribeDataReceived<IOBDData>((sender, response)
                => Console.WriteLine($"RECEIVED DATA: {response.Data} (PID: {response.Data.PID})\n"));

            conn.DataReceived += (data, args) =>
            {
                //var asHex = ELMDataToHexString(args);
                var asStr = ELMDataToString(args);

                //Console.WriteLine($"BYTE: {asHex}");
                Console.WriteLine($"TEXT: {asStr}\n");
            };

            obd2.Initialize();
            await GetSupportedPids(obd2);

            string? command = string.Empty;
            while (GetInput(ref command) && command is not null)
            {
                if (command.Equals("quit", StringComparison.CurrentCultureIgnoreCase))
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
                        await obd2.WaitQueueAsync();
                    }
                    else
                    {
                        if (byte.TryParse(command, out var pid))
                        {
                            if (_supportedPids.Contains(pid))
                                await RequestData(pid, obd2);
                        }
                        await obd2.WaitQueueAsync();
                    }
                }
            }
        }

        static string ELMDataToHexString(DataReceivedEventArgs? args)
            => string.Join(' ', args?.Data?.Take(args?.Count ?? 0)?.Select(b => $"{b:X2}") ?? []);

        static string ELMDataToString(DataReceivedEventArgs? args) 
        {
            var data = string.Join("", args?.Data?
                .Take(args?.Count ?? 0)?
                .Select(b => (char)b) ?? []);

            return SymbolDisplay.FormatLiteral(data, false);
        }

        static bool GetInput(ref string? command)
        {
            Console.Write("ready > ");
            var res = !string.IsNullOrWhiteSpace(command = Console.ReadLine()?.ToUpper());
            Console.WriteLine();
            return res;
        }

        static async Task GetSupportedPids(ELM327 obd2)
        {
            obd2.SubscribeDataReceived<PidsSupported01_20>((sender, response) => _supportedPids.AddRange(response.Data.SupportedPids));
            obd2.SubscribeDataReceived<PidsSupported21_40>((sender, response) => _supportedPids.AddRange(response.Data.SupportedPids));
            obd2.SubscribeDataReceived<PidsSupported41_60>((sender, response) => _supportedPids.AddRange(response.Data.SupportedPids));
            obd2.SubscribeDataReceived<PidsSupported61_80>((sender, response) => _supportedPids.AddRange(response.Data.SupportedPids));
            obd2.SubscribeDataReceived<PidsSupported81_A0>((sender, response) => _supportedPids.AddRange(response.Data.SupportedPids));
            obd2.SubscribeDataReceived<PidsSupportedA1_C0>((sender, response) => _supportedPids.AddRange(response.Data.SupportedPids));
            obd2.SubscribeDataReceived<PidsSupportedC1_E0>((sender, response) => _supportedPids.AddRange(response.Data.SupportedPids));

            await RequestData<PidsSupported01_20>(obd2);
            await RequestData<PidsSupported21_40>(obd2);
            await RequestData<PidsSupported41_60>(obd2);
            await RequestData<PidsSupported61_80>(obd2);
            await RequestData<PidsSupported81_A0>(obd2);
            await RequestData<PidsSupportedA1_C0>(obd2);
            await RequestData<PidsSupportedC1_E0>(obd2);

            obd2.UnsubscribeDataReceived<PidsSupported01_20>((sender, response) => { });
            obd2.UnsubscribeDataReceived<PidsSupported21_40>((sender, response) => { });
            obd2.UnsubscribeDataReceived<PidsSupported41_60>((sender, response) => { });
            obd2.UnsubscribeDataReceived<PidsSupported61_80>((sender, response) => { });
            obd2.UnsubscribeDataReceived<PidsSupported81_A0>((sender, response) => { });
            obd2.UnsubscribeDataReceived<PidsSupportedA1_C0>((sender, response) => { });
            obd2.UnsubscribeDataReceived<PidsSupportedC1_E0>((sender, response) => { });
        }

        static async Task RequestData<T>(ELM327 obd2) where T : class, IOBDData, new()
        {
            Console.WriteLine($"Requesting data for PID: {typeof(T).Name} ({new T().PID})\n");
            await obd2.RequestDataAsync<T>();
            await obd2.WaitQueueAsync();
        }

        static async Task RequestData(byte pid, ELM327 obd2)
        {
            var type = _pidTypes[pid];
            Console.WriteLine($"Requesting data for PID: {type.Name} ({pid})\n");
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
