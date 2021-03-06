﻿using System;
using System.Collections.Generic;
using System.Collections;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using MinecraftBots.Bot;
using MinecraftBots.Protocol.Server;
using MinecraftBots.Protocol.Client.Handler;
using MinecraftBots.Net;
using MinecraftBots.Protocol.Server.Forge;
using System.Threading.Tasks;
using MinecraftBots.Protocol.Client;
using Starksoft.Net.Proxy;

namespace MinecraftBots.Bot
{
    class tBotsTask_b
    {
        int threads;
        public List<MinecraftBot> Bots = new List<MinecraftBot>();

        ArrayList Chat;
        List<string> proxyip = new List<string>();
        List<TcpClient> socks_pool = new List<TcpClient>();
        string username;
        public bool TaskWorking = true;
        public static ServerInfo Info;
        public static int protocol;
        public tBotsTask_b(ServerInfo info, string player, int thr, ArrayList chatlist, int v = 0)
        {
            Chat = chatlist;
            username = player;
            Info = info;
            threads = thr;
            if (Setting.wlogs)
                Helper.WriteLogs(Info.ServerIP, Info.ServerPort, Info.GameVersion, thr);
            if (v == 0)
                protocol = Info.ProtocolVersion;
            else
                protocol = v;
        }
        internal void newTask(int cooldown = 0)
        {
            MinecraftBot client;
            Thread socksThread = new Thread(getProxys);
            socksThread.Start();
            new Thread(new ThreadStart(Clear)).Start();
            new Thread(new ThreadStart(() =>
            {
                try
                {
                    while (TaskWorking)
                    {
                        while (Bots.Count < threads)
                        {
                            if (socks_pool.Count > 0)
                            {
                                client = new MinecraftBot(socks_pool[0], Info.ServerIP, Info.ServerPort, protocol, Info.ForgeInfo, this);
                                Bots.Add(client);
                                client.Chatlist = Chat;
                                client.AddPlayer(username);
                                socks_pool.RemoveAt(0);
                                Thread.Sleep(cooldown);
                            }
                            else
                            {
                                Thread.Sleep(1000);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    ConsoleIO.AddMsgSeq(e.Message, "Error");
                }
            })).Start();
        }

        public void getProxys()
        {
            while (TaskWorking)
            {
                if (proxyip.Count == 0)
                {
                    HttpReq.getips_(proxyip);
                }
                foreach (string socks in proxyip.ToArray())
                {
                    Thread.Sleep(10);
                    if (socks_pool.Count < 50)//预存Connections
                    {
                        new Thread(new ThreadStart(() =>
                        {
                            string ProxyIP;
                            int ProxyPort;
                            string[] ip = socks.Split(':');
                            if (ip.Length == 2)
                            {
                                ProxyIP = ip[0];
                                ProxyPort = int.Parse(ip[1]);
                            }
                            else
                            {
                                ProxyIP = ip[0];
                                ProxyPort = 8080;
                            }
                            try
                            {
                                TcpClient client = new TcpClient();
                                client.ReceiveTimeout = 5000;
                                client.Connect(ProxyIP, ProxyPort);
                                string requeststr = string.Format("CONNECT " + Info.ServerIP + ":" + Info.ServerPort + " HTTP/1.1\r\nHost: " + Info.ServerIP + ":" + Info.ServerPort + "\r\nProxy-Connection: keep-alive\r\n\r\n");
                                if (client.Connected)
                                {
                                    byte[] request = Encoding.UTF8.GetBytes(requeststr);
                                    NetworkStream stream = client.GetStream();
                                    client.Client.Send(request);
                                    byte[] response = new byte[1024];
                                    int l = stream.Read(response, 0, response.Length);
                                    string read = Encoding.Default.GetString(response, 0, l);
                                    if (read.Contains("200") || read.Contains("100"))
                                        socks_pool.Add(client);
                                }
                            }
                            catch { }

                        })).Start();
                        proxyip.Remove(socks);
                    }
                    else
                    {
                        break;
                    }
                }
                Thread.Sleep(3000);
            }
        }
        private void Clear()
        {
            while (TaskWorking)
            {
                Thread.Sleep(Setting.t_clear);
                int l = 0;
                int clear = 0;
                for (; l < Bots.Count; l++)
                {
                    TimeSpan ts = DateTime.Now.Subtract(Bots[l].alivetime);
                    if (ts.Seconds > 25)
                    {
                        Bots[l].Dispose();
                        clear++;
                    }
                }
                ConsoleIO.AddMsgSeq("清理线程数目:" + clear, "Thread");
                ConsoleIO.AddMsgSeq("当前运行线程:" + Bots.Count, "Thread");
                ConsoleIO.AddMsgSeq("代理数量:" + proxyip.Count, "Thread");
                GC.Collect();
            }
        }
        public class MinecraftBot : MinecraftEvent, IDisposable
        {
            TcpClient Tcp;
            public DateTime alivetime = DateTime.Now;
            int ProtocolVersion;
            string Host;
            int Port;
            string playerID = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_";
            string playername;
            ForgeInfo forgeInfo;
            MinecraftProtocol client;
            public ArrayList Chatlist;
            tBotsTask_b bots;
            Thread tabComplete;
            Thread chatMsg;
            internal MinecraftBot(TcpClient tcp, string host, int port, int protocolver, ForgeInfo forge, tBotsTask_b s)
            {
                this.Tcp = tcp;
                ProtocolVersion = protocolver;
                Host = host;
                Port = port;
                forgeInfo = forge;
                bots = s;
            }
            internal bool AddPlayer(string username = null)
            {
                if (Tcp.Connected)
                {
                    if (username != null)
                        playername = username.Replace("%RANDOM%", randomName());
                    try
                    {
                        client = new MinecraftProtocol(Tcp, ProtocolVersion, this, forgeInfo);
                        if (client.Login(Host, Port, playername))
                        {
                            if (Setting.t_tabcomplete > 0)
                            {
                                TabComplete();
                            }
                            return true;
                        }
                    }
                    catch
                    {
                        Dispose();
                    }
                }
                else
                {
                    Dispose();
                }
                return false;
            }
            public void OnGameJoin()
            {
                ConsoleIO.AddMsgSeq(playername + " Join the game", "Player");
                if (Setting.sendsetting)
                    client.SendClientSettings("en_US", 9, 0, 0, false, 65, 0);
                alivetime = DateTime.Now;
                chatMsg = new Thread(new ThreadStart(() =>
                {
                    while (Tcp != null && Tcp.Connected)
                    {
                        for (int l = 0; l < Chatlist.Count; l++)
                        {
                            Thread.Sleep(2000);
                            client.SendChatMessage(Chatlist[l].ToString());
                        }
                    }
                }));
                chatMsg.IsBackground = true;
                chatMsg.Start();
            }

            public void OnKeepAlive()
            {
                alivetime = DateTime.Now;
            }
            public void OnConnectionLost(BotUtils.DisconnectReason type, string msg)
            {
                if (type == BotUtils.DisconnectReason.InGameKick)
                {
                    ConsoleIO.AddMsgSeq(string.Format("{0}连接丢失:{1}", playername, Helper.ParseMsg(msg)), "Connection");
                    if (Setting.t_rejoin > 0)
                        ReJoin();
                    else
                        Dispose();
                }
                else if (type == BotUtils.DisconnectReason.LoginRejected)
                {
                    ConsoleIO.AddMsgSeq(string.Format("{0}拒绝连接:{1}", playername, Helper.ParseMsg(msg)), "Connection");
                    if (Setting.t_rejoin > 0)
                        ReJoin();
                    else
                        Dispose();
                }
                else if (type == BotUtils.DisconnectReason.ConnectionLost)
                {
                    Dispose();
                }
            }
            private void ReJoin()
            {
                if (Setting.t_rejoin < 1000)
                {
                    Thread.Sleep(Setting.t_rejoin);
                    alivetime = DateTime.Now;
                }
                else
                {
                    int sec = Setting.t_rejoin / 1000;
                    for (int i = 0; i < sec; i++)
                    {
                        Thread.Sleep(1000);
                        alivetime = DateTime.Now;
                    }
                }
                AddPlayer();
            }
            public void SendMotd(TcpClient tcp)
            {
                byte[] packet_id = ProtocolHandler.getVarInt(0);
                byte[] protocol_version = ProtocolHandler.getVarInt(-1);
                byte[] server_adress_val = Encoding.UTF8.GetBytes(Host);
                byte[] server_adress_len = ProtocolHandler.getVarInt(server_adress_val.Length);
                byte[] server_port = BitConverter.GetBytes((ushort)Port); Array.Reverse(server_port);
                byte[] next_state = ProtocolHandler.getVarInt(1);
                byte[] packet2 = ProtocolHandler.concatBytes(packet_id, protocol_version, server_adress_len, server_adress_val, server_port, next_state);
                byte[] tosend = ProtocolHandler.concatBytes(ProtocolHandler.getVarInt(packet2.Length), packet2);

                tcp.Client.Send(tosend, SocketFlags.None);

                byte[] status_request = ProtocolHandler.getVarInt(0);
                byte[] request_packet = ProtocolHandler.concatBytes(ProtocolHandler.getVarInt(status_request.Length), status_request);

                tcp.Client.Send(request_packet, SocketFlags.None);

                ProtocolHandler handler = new ProtocolHandler(tcp);
                int packetLength = handler.readNextVarIntRAW();
            }
            private void TabComplete()
            {
                tabComplete = new Thread(new ThreadStart(() =>
                {
                    while (Tcp != null && Tcp.Connected)
                    {
                        Thread.Sleep(Setting.t_tabcomplete);
                        client.AutoTabComplete("/");
                    }
                }));
                tabComplete.IsBackground = true;
                tabComplete.Start();
            }
            private string randomName()
            {
                Random random = new Random();
                int name_len = random.Next(5, 15);
                string id = "";
                for (int l = 0; l < name_len; l++)
                {
                    id += playerID[random.Next(0, playerID.Length - 1)];
                }
                return id;
            }

            public void Dispose()
            {
                if (client != null)
                    client.Dispose();
                else
                {
                    if (Tcp != null)
                        Tcp.Close();
                }
                Tcp = null;
                if (chatMsg != null)
                    chatMsg.Abort();
                if (tabComplete != null)
                    tabComplete.Abort();
                if (bots.Bots.Contains(this))
                    bots.Bots.Remove(this);
                //GC.SuppressFinalize(this);
            }
        }
    }
}