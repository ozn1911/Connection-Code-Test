
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;





#nullable enable
public class NetworkCom
{
    public event Action<object> MessageRecieved;
    public Dictionary<Type, Action<object>> FuncDict = new Dictionary<Type, Action<object>>();
    public class Connection
    {
        public TcpClient TcpConnection;
        public Dictionary<Type, Action<object>> FuncDict = new Dictionary<Type, Action<object>>();
        public event Action<object> MessageRecieved;
        public event Action<object> Disconnected;
        public async Task SendMessage(object obj)
        {
            byte[] data = NetworkCom.MakePacket(obj);
            await TcpConnection.GetStream().WriteAsync(data, 0, data.Length);
        }
    }

    public struct DisconnectReason
    {
        public string reason;
    }
    struct SharePacket
    {
        public string type;
        public byte[] data;
    }

    public static byte[] MakePacket(object obj)
    {
        var temp = new SharePacket
        {
            type = obj.GetType().Name,
            data = JsonSerializer.SerializeToUtf8Bytes(obj)
        };
        var data = JsonSerializer.SerializeToUtf8Bytes(temp);
        return data;
    }
    public static async Task<object?> ReturnPacket(byte[] data)
    {
        try
        {
            var e = JsonDocument.Parse(Encoding.UTF8.GetString(data));
            var c = Type.GetType(e.RootElement.GetProperty("type").GetRawText());
            if (c != null)
            {
                var bytedata = e.RootElement.GetProperty("data").GetBytesFromBase64();
                var z = await Task.Run(() => { return JsonSerializer.Deserialize(bytedata, c); }).WaitAsync(CancellationToken.None);
                if (z != null)
                {
                    return z;
                }
            }
        }
        catch
        {

        }
        return null;
    }
    public async Task MessageLoop(Connection client)
    {
        byte[] buffer = new byte[1024];
        int bytesRead;
        var stream = client.TcpConnection.GetStream();
        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            var temp = ReturnPacket(buffer);
            Type type = temp.GetType();
            if (FuncDict.TryGetValue(type, out var method)) method?.Invoke(temp);
            MessageRecieved?.Invoke(temp);

        }
    }

    public static async Task<UdpClient> GetUdpClient(string ip)
    {
        IPEndPoint endpoint = new IPEndPoint(IPAddress.Parse(ip), 4001);
        UdpClient client = new UdpClient(endpoint);
        return client;
    }
}



public class ComServer : NetworkCom
{
    public TcpListener Listener;
    bool AcceptingClients = false;
    public void StartAcceptingClients()
    {
        AcceptingClients = true;
    }
    public void StopAcceptingClients()
    {
        AcceptingClients = false;
    }
    bool IdBased = true;
    public Dictionary<string, Connection> Connections = new Dictionary<string, Connection>();
    public int maxConnections = 4;
    public List<string> IPBanDict = new List<string>();

    bool amirunning = true;

    public event Action ClientConnected;
    public ComServer(int port, int maxConnectionNumber = 4, bool isBasedOnID = true)
    {
        IdBased = isBasedOnID;
        maxConnections = maxConnectionNumber;
        Listener = new TcpListener(IPAddress.Any, port);
        Listener.Start();
        Task.Run(RecieveClientLoop);


    }
    public async Task RecieveClientLoop()
    {
        while (amirunning)
        {
            var temp = await Listener.AcceptTcpClientAsync();
            var endpoint = temp.Client.RemoteEndPoint as IPEndPoint;
            if (endpoint == null)
            {
                temp.Close();
                continue;
            }
            var ip = endpoint!.Address.ToString();
            if (!AcceptingClients)
            {
                temp.Close();
            }
            else
            if (IPBanDict.Contains(ip))
            {
                byte[] data = NetworkCom.MakePacket(new DisconnectReason { reason = $"IP adress found in IPBanDict!!!" });
                if (temp.Connected)
                {
                    temp.GetStream().WriteAsync(data, 0, data.Length);
                }
                temp.Close();
            }
            if (!IdBased && Connections.ContainsKey(ip))
            {
                continue;
            }
            else
            {
                HandleClientJoin(temp);
            }
        }
        return;
    }
    public void Broadcast(object obj)
    {
        List<string> disconnects = new List<string>();
        foreach (var conn in Connections)
        {
            Send(obj, conn.Key);
        }
    }

    void HandleClientLeave(string adress)
    {
        if (!Connections.TryGetValue(adress, out var connection)) return;
        connection.TcpConnection.Close();
        connection.TcpConnection.Dispose();
        Connections.Remove(adress);
    }


    async Task HandleClientJoin(TcpClient Client)
    {
        ClientConnected?.Invoke();
        var temp = new Connection { TcpConnection = Client };
#pragma warning disable CS4014 //fire & forget
        MessageLoop(temp);
#pragma warning restore CS4014

        string id = "";

        if (IdBased)
        {
            for (int i = 0; i < maxConnections; i++)
            {
                if (!Connections.ContainsKey(i.ToString()))
                {
                    id = i.ToString();
                    break;
                }
            }
        }
        else
        {
            id = (temp.TcpConnection.Client.RemoteEndPoint as IPEndPoint).Address.ToString();
        }
        Connections.Add(id, temp);
    }

    public async Task Send(object obj, string adress)
    {
        if (!Connections.TryGetValue(adress, out var connection)) return;
        if (!connection.TcpConnection.Connected)
        {
            HandleClientLeave(adress);
            return;
        }



        byte[] data = NetworkCom.MakePacket(obj);

        await connection?.TcpConnection.GetStream().WriteAsync(data, 0, data.Length);
    }

}
public class ComClient : NetworkCom
{

    public NetworkCom parent;
    public TcpClient Client = new TcpClient();
    public string ip;
    public NetworkStream stream;
    public Connection connection;
    public async Task AttempJoinServer(string host, int port)
    {
        Client = new TcpClient();
        await Client.ConnectAsync(host, port);
#pragma warning disable CS4014 //fire & forget
        var temp = new Connection { TcpConnection = Client };
        MessageLoop(temp);
#pragma warning restore CS4014
        return;
    }
    public async Task Send(object obj)
    {
        byte[] data = NetworkCom.MakePacket(obj);

        await Client.GetStream().WriteAsync(data, 0, data.Length);
    }
}
