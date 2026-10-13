using System.Net;

namespace NModbus
{
    /// <summary>
    ///     Receives the raw bytes a slave network reads from and writes to its masters, for example
    ///     to show the traffic. Calls come from the network's listen threads.
    /// </summary>
    public interface IModbusTrafficObserver
    {
        /// <summary>
        ///     Called after each read from <paramref name="remote"/>: one datagram, or one chunk of a TCP stream.
        /// </summary>
        void OnReceived(EndPoint remote, byte[] buffer, int offset, int count);

        /// <summary>
        ///     Called after each frame written to <paramref name="remote"/>.
        /// </summary>
        void OnSent(EndPoint remote, byte[] buffer, int offset, int count);
    }
}
