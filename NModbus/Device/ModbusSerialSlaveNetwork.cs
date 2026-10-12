using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NModbus.Logging;
using NModbus.Message;

namespace NModbus.Device
{
    using Extensions;

    internal class ModbusSerialSlaveNetwork : ModbusSlaveNetwork
    {
        private readonly IModbusSerialTransport _serialTransport;
        private readonly IModbusFactory _modbusFactory;

        public ModbusSerialSlaveNetwork(IModbusSerialTransport transport, IModbusFactory modbusFactory, IModbusLogger logger) 
            : base(transport, modbusFactory, logger)
        {
            _serialTransport = transport ?? throw new ArgumentNullException(nameof(transport));
            _modbusFactory = modbusFactory;
        }

        private IModbusSerialTransport SerialTransport => _serialTransport;

        public override Task ListenAsync(CancellationToken cancellationToken = new CancellationToken())
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // read request and build message
                    byte[] frame;
                    try
                    {
                        frame = SerialTransport.ReadRequest();
                    }
                    catch (TimeoutException)
                    {
                        // Idle line times out every ReadTimeout: no log, would flood.
                        // Partial frame timing out lands here too.
                        TryDiscardInBuffer();
                        continue;
                    }

                    //Create the request
                    IModbusMessage request = _modbusFactory.CreateModbusRequest(frame);

                    //Check the message
                    if (SerialTransport.CheckFrame && !SerialTransport.ChecksumsMatch(request, frame))
                    {
                        string msg = $"Checksums failed to match {string.Join(", ", request.MessageFrame)} != {string.Join(", ", frame)}.";
                        Logger.Warning(msg);
                        throw new IOException(msg);
                    }

                    //Apply the request
                    IModbusMessage response = ApplyRequest(request);

                    if (response == null)
                    {
                        _serialTransport.IgnoreResponse();
                    }
                    else
                    {
                        Transport.Write(response);
                    }
                }
                catch (IOException ioe)
                {
                    // CRC mismatch thrown above lands here.
                    if (cancellationToken.IsCancellationRequested) break;
                    Logger.Warning($"IO Exception encountered while listening for requests - {ioe.Message}");
                    TryDiscardInBuffer();
                }
                catch (TimeoutException te)
                {
                    // Write or IgnoreResponse timeout. Warning: ModbusSim drops Trace.
                    Logger.Warning($"Timeout Exception encountered while listening for requests - {te.Message}");
                    TryDiscardInBuffer();
                }
                catch(InvalidOperationException)
                {
                    // when the underlying transport is disposed
                    break;
                }
                catch (Exception ex)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    Logger.Error($"{GetType()}: {ex.Message}");
                    TryDiscardInBuffer();
                }
            }

            return Task.FromResult(0);
        }

        private void TryDiscardInBuffer()
        {
            // Swallow disposed/invalid-state errors here.
            try
            {
                SerialTransport.DiscardInBuffer();
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }
    }
}