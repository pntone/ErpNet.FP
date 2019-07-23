﻿using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;

namespace ErpNet.FP.Core.Transports
{
    public class ComTransport : Transport
    {
        public override string TransportName => "com";

        private readonly IDictionary<string, ComTransport.Channel?> openedChannels =
            new Dictionary<string, ComTransport.Channel?>();

        public override IChannel OpenChannel(string address)
        {
            if (openedChannels.TryGetValue(address, out Channel? channel))
            {
                if (channel == null)
                {
                    throw new TimeoutException("disabled due to timeout");
                }
                return channel;
            }
            else
            {
                try
                {
                    channel = new Channel(address);
                    openedChannels.Add(address, channel);
                    return channel;
                }
                catch (TimeoutException e)
                {
                    openedChannels.Add(address, null);
                    throw e;
                }
            }
        }

        public override void Drop(IChannel channel)
        {
            ((Channel)channel).Close();
        }

        /// <summary>
        /// Returns all serial com port addresses, which can have connected fiscal printers. 
        /// The returned pairs are in the form <see cref="KeyValuePair{Address, Description}"/>.
        /// </summary>
        /// <value>
        /// All available addresses and descriptions.
        /// </value>
        public override IEnumerable<(string address, string description)> GetAvailableAddresses()
        {
            // For description of com ports we do not have anything else than the port name / path
            // So we will return port name as description too.
            return from address in SerialPort.GetPortNames() select (address, address);
        }

        public class Channel : IChannel
        {
            internal readonly SerialPort serialPort;
            protected Timer idleTimer;

            public string Descriptor => serialPort.PortName;

            public Channel(string portName, int baudRate = 115200, int timeout = 600)
            {
                serialPort = new SerialPort
                {
                    // Allow the user to set the appropriate properties.
                    PortName = portName,
                    BaudRate = baudRate,

                    // Set the read/write timeouts
                    ReadTimeout = timeout,
                    WriteTimeout = timeout
                };

                idleTimer = new Timer(IdleTimerElapsed);
            }

            private void IdleTimerElapsed(object state)
            {
                System.Diagnostics.Trace.WriteLine($"Idle timer elapsed for the com port {serialPort.PortName}");
                Close();
            }

            public void Open()
            {
                System.Diagnostics.Trace.WriteLine($"Opening the com port {serialPort.PortName}");
                serialPort.Open();
            }

            public void Close()
            {
                if (serialPort.IsOpen)
                {
                    System.Diagnostics.Trace.WriteLine($"Closing the com port {serialPort.PortName}");
                    serialPort.DiscardInBuffer();
                    serialPort.DiscardOutBuffer();
                    serialPort.Close();
                    serialPort.Dispose();
                }
            }

            public void Dispose()
            {
                idleTimer.Dispose();
                Close();
            }

            /// <summary>
            /// Reads data from the com port.
            /// </summary>
            /// <returns>The data which was read.</returns>
            public byte[] Read()
            {
                var buffer = new byte[serialPort.ReadBufferSize];
                var task = serialPort.BaseStream.ReadAsync(buffer, 0, buffer.Length);
                if (task.Wait(serialPort.ReadTimeout))
                {
                    var result = new byte[task.Result];
                    Array.Copy(buffer, result, task.Result);
                    idleTimer.Change(1000, 0);
                    return result;
                }
                var errorMessage = $"Timeout occured while reading from com port '{serialPort.PortName}'";
                System.Diagnostics.Trace.WriteLine(errorMessage);
                throw new TimeoutException(errorMessage);
            }

            /// <summary>
            /// Writes the specified data to the com port.
            /// </summary>
            /// <param name="data">The data to write.</param>
            public void Write(byte[] data)
            {
                if (!serialPort.IsOpen)
                {
                    Open();
                }
                serialPort.DiscardInBuffer();
                var bytesToWrite = data.Length;
                while (bytesToWrite > 0)
                {
                    var writeSize = Math.Min(bytesToWrite, serialPort.WriteBufferSize);
                    var task = serialPort.BaseStream.WriteAsync(
                        data,
                        data.Length - bytesToWrite,
                        writeSize
                    );
                    if (task.Wait(serialPort.WriteTimeout))
                    {
                        bytesToWrite -= writeSize;
                    }
                    else
                    {
                        var errorMessage = $"Timeout occured while writing to com port '{serialPort.PortName}'";
                        System.Diagnostics.Trace.WriteLine(errorMessage);
                        throw new TimeoutException(errorMessage);
                    }
                }
            }
        }

    }
}