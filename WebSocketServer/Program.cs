using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BlockchainLib.Business.Implementation
{
    public class WebSocketServer
    {
        private TcpListener server = null;
        public string IpAddress { get; set; }
        public int port { get; set; }
        static Dictionary<string, TcpClient> connectedClients = new Dictionary<string, TcpClient>();

        public WebSocketServer()
        {
            //no-arg constructor

        }

        public WebSocketServer(string IpAddress, int port)
        {
            this.IpAddress = IpAddress;
            this.port = port;

        }

        // helper function to encode the message according to Websocket's standard
        private static Byte[] encodeOutgoingMessage(String outgoingMessage)
        {
            Byte[] dataToSend = Encoding.UTF8.GetBytes(outgoingMessage);
            Byte[] frame = null;

            int indexStartRawData = -1;

            if (dataToSend.Length <= 125)
            {
                frame = new Byte[2 + dataToSend.Length];
                frame[0] = 129;
                frame[1] = (Byte)dataToSend.Length;
                indexStartRawData = 2;
            }
            else if (dataToSend.Length >= 126 && dataToSend.Length <= 65535)
            {
                frame = new Byte[4 + dataToSend.Length];
                frame[0] = 129;
                frame[1] = 126;
                frame[2] = (Byte)((Byte)(dataToSend.Length >> 8) & 255);
                frame[3] = (Byte)((Byte)(dataToSend.Length) & 255);
                indexStartRawData = 4;
            }
            else
            {
                frame = new Byte[9 + dataToSend.Length];
                frame[0] = 129;
                frame[1] = 127;
                frame[2] = (Byte)((Byte)(dataToSend.Length >> 56) & 255);
                frame[3] = (Byte)((Byte)(dataToSend.Length >> 48) & 255);
                frame[4] = (Byte)((Byte)(dataToSend.Length >> 40) & 255);
                frame[5] = (Byte)((Byte)(dataToSend.Length >> 32) & 255);
                frame[6] = (Byte)((Byte)(dataToSend.Length >> 24) & 255);
                frame[7] = (Byte)((Byte)(dataToSend.Length >> 16) & 255);
                frame[8] = (Byte)((Byte)(dataToSend.Length >> 8) & 255);
                frame[9] = (Byte)((Byte)(dataToSend.Length) & 255);
                indexStartRawData = 10;


            }

            Buffer.BlockCopy(dataToSend, 0, frame, indexStartRawData, dataToSend.Length);
            return frame;

        }


        //Helper function to decode message coming from client
        private static string decodeMessage(Byte[] incomingMessage)
        {

            //assume that message is a text message i.e. opcode 129.
            Byte[] masks = new Byte[4];
            Byte[] data = null;
            int dataLength = incomingMessage[1] & 127;
            int indexFirstMask = 2;
            int indexFirstDataByte = -1;

            if (dataLength == 126)
                indexFirstMask = 4;

            else if (dataLength == 127)
                indexFirstMask = 10;

            Buffer.BlockCopy(incomingMessage, indexFirstMask, masks, 0, 4);

            indexFirstDataByte = indexFirstMask + 4;

            data = new Byte[incomingMessage.Length - indexFirstDataByte];

            for (int i = indexFirstDataByte, j = 0; i < incomingMessage.Length; i++, j++)
            {

                data[j] = (byte)(incomingMessage[i] ^ masks[j % 4]);


            }


            return Encoding.UTF8.GetString(data);


        }

        private void ListenForClients()
        {

            this.server.Start();
            Console.WriteLine("Server has started on 127.0.0.1:80.{0}Waiting for a connection...", Environment.NewLine);
            while (true)
            {


                TcpClient client = server.AcceptTcpClient();
                Thread clientThread = new Thread(new ParameterizedThreadStart(handleClient));
                clientThread.Start(client);

            }


        }

        private void handleClient(Object newClient)
        {

            Console.WriteLine("A client connected.");

            TcpClient client = (TcpClient)newClient;
            connectedClients.Add(client.Client.RemoteEndPoint.ToString(), client);
            NetworkStream stream = client.GetStream();

            //enter to an infinite cycle to be able to handle every change in stream
            while (true)
            {
                while (!stream.DataAvailable) ;

                Byte[] bytes = new Byte[client.Available];
                stream.Read(bytes, 0, bytes.Length);
                String data = Encoding.UTF8.GetString(bytes);

                if (new Regex("^GET").IsMatch(data))
                {
                    Console.WriteLine("handshake response");
                    Byte[] response = Encoding.UTF8.GetBytes("HTTP/1.1 101 Switching Protocols" + Environment.NewLine
                        + "Connection: Upgrade" + Environment.NewLine
                        + "Upgrade: websocket" + Environment.NewLine
                        + "Sec-WebSocket-Accept: " + Convert.ToBase64String(
                            SHA1.Create().ComputeHash(
                                Encoding.UTF8.GetBytes(
                                    new Regex("Sec-WebSocket-Key: (.*)").Match(data).Groups[1].Value.Trim() + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"
                                )
                            )
                        ) + Environment.NewLine
                        + Environment.NewLine);

                    stream.Write(response, 0, response.Length);
                }
                else
                {

                    //Decode the message and if it is equal to  "Tear Down" then remove particular client from Dictionary of ConnectedClients
                    string decodedMessage = decodeMessage(bytes);

                    if (decodedMessage.Equals("Tear Down"))
                    {

                        Console.WriteLine("message from client : " + client.Client.RemoteEndPoint + " : " + decodedMessage);
                        connectedClients.Remove(client.Client.RemoteEndPoint.ToString());
                        break;

                    }

                }
            }

            client.Close();


        }

        public void DispatchNotification(Object notification)
        {
            Debug.WriteLine("connected cliens count is : " + connectedClients.Count);
            foreach (var client in connectedClients)
            {
                Thread DispatcherThread = new Thread(

                     () => sendNotificationToClient(client.Value, notification)

                );

                DispatcherThread.Start();

            }

        }

        private void sendNotificationToClient(Object connectedClient, Object notification)
        {

            try
            {
                TcpClient client = (TcpClient)connectedClient;
                NetworkStream clientStream = client.GetStream();
                Byte[] frame = encodeOutgoingMessage(notification.ToString());
                clientStream.Write(frame, 0, frame.Length);

            }
            catch (Exception e)
            {
                Debug.WriteLine("problem in sending notification : " + e.Message);

            }


        }

        public void StartServer()
        {
            if (server == null)
            {
                server = new TcpListener(IPAddress.Parse(this.IpAddress), this.port);
                Thread listenThread = new Thread(new ThreadStart(ListenForClients));
                listenThread.Start();
            }

        }
    }
}
