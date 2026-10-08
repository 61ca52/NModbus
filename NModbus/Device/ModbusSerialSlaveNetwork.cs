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
                    byte[] frame = SerialTransport.ReadRequest();

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
                catch (ObjectDisposedException)
                {
                    // The underlying SerialPort/stream was disposed.
                    // Treat the same as InvalidOperationException.
                    break;
                }
                catch (IOException)
                {
                    // Idle/partial-frame conditions (e.g. "Read resulted in 0 bytes returned",
                    // sporadic line noise) surface as IOException. These are the slave's normal
                    // wait state — logging them flooded the timeline. Genuine framing/CRC
                    // problems still throw their own IOException with a "Checksums failed to
                    // match" payload and reach the fallthrough Error branch via ApplyRequest.
                    if (cancellationToken.IsCancellationRequested) break;
                    TryDiscardInBuffer();
                }
                catch (TimeoutException)
                {
                    // The serial port's read timeout firing is the expected heartbeat of an
                    // idle listener — it just means no master spoke during the last
                    // ReadTimeout window. Silently flush and loop; no log line.
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