using System.Net;
using System.Net.Sockets;
using Chat.Common;
using Chat.Common.MessageHandlers;

namespace ChatServer;

public class ChatServer(IPAddress address, int port)
{
    public async Task Run(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, port);

        try
        {
            listener.Start();
            Console.WriteLine($"Server listening on port {port}");

            while (!ct.IsCancellationRequested)
            {
                Console.WriteLine("Waiting for first client...");
                TcpClient client1 = await listener.AcceptTcpClientAsync(ct);
                Console.WriteLine("Client 1 connected");

                Console.WriteLine("Waiting for second client...");
                TcpClient client2 = await listener.AcceptTcpClientAsync(ct);
                Console.WriteLine("Client 2 connected");


                await HandleClientsAsync(client1, client2, ct);
                Console.WriteLine("Chat ended, waiting for new clients");
            }
        }
        catch(OperationCanceledException)
        {
            Console.WriteLine("Server shutting down...");
        }
        catch(Exception ex)
        {
            Console.WriteLine($"Serwer Error {ex.Message}");
        }
        finally
        {
            listener.Stop();
            Console.WriteLine("Server stopped");
        }
    }


    static async Task HandleClientsAsync(TcpClient client1, TcpClient client2, CancellationToken ct)
    {
        await using var stream1 = client1.GetStream();
        await using var stream2 = client2.GetStream();

        using var messageForwardCts = new CancellationTokenSource();

        using var readerFromClient1 = new MessageReader(stream1);
        using var readerFromClient2 = new MessageReader(stream2);

        using var writerToClient1 = new MessageWriter(stream1);
        using var writerToClient2 = new MessageWriter(stream2);

        
        var t1 = ForwardMessagesAsync(readerFromClient1, writerToClient2, messageForwardCts.Token);
        var t2 = ForwardMessagesAsync(readerFromClient2, writerToClient1, messageForwardCts.Token);
        var cancellationTask = Task.Delay(-1, ct);

        var finishedTask = await Task.WhenAny(t1, t2, cancellationTask);

        if (finishedTask == cancellationTask)
        {
            await SendCancellationNotification(writerToClient1);
            await SendCancellationNotification(writerToClient2);
        }
        else
        {
            var remainingClientWriter = finishedTask == t1 ? writerToClient2 : writerToClient1;
            await SendPeerDisconnectedNotification(remainingClientWriter, ct);
        }

        if (!messageForwardCts.IsCancellationRequested)
            await messageForwardCts.CancelAsync();
        
        await Task.WhenAll(t1, t2);
        
        Console.WriteLine("Clients disconnected, pair handler finished.");
    }
    
    
    static async Task ForwardMessagesAsync(MessageReader reader, MessageWriter writer, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                MessageDTO? msg = await reader.ReadMessage(ct);
                if (msg == null)
                    return;

                Console.WriteLine($"[{msg.Time:u}] {msg.Sender} : {msg.Content}");

                await writer.WriteMessage(msg, ct);
                
            }
        }
        catch(OperationCanceledException)
        {
            return;
        }
        catch(Exception ex)
        {
            Console.WriteLine($"Exception {ex.Message}");
        }
    }


    static async Task SendCancellationNotification(MessageWriter writer1)
    {
        using var cts = new CancellationTokenSource(300);
        
        var notification = new MessageDTO
        {
            Content = "Server shutting down.",
            Sender = "Server",
            Time = DateTime.UtcNow,
        };

        try
        {
            await writer1.WriteMessage(notification, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Failed to send cancellation notification.");
        }
        catch (IOException e)
        {
            Console.WriteLine($"Error while sending cancellation notification: {e.Message}");
        }
    }
    
    
    static async Task SendPeerDisconnectedNotification(MessageWriter writer, CancellationToken ct)
    {
        var disconnectNotification = new MessageDTO
        {
            Content = "PEER DISCONNECTED",
            Sender = "Server",
            Time = DateTime.UtcNow,
        };
        
        try
        {
            await writer.WriteMessage(disconnectNotification, ct);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Failed to send disconnection notification.");
        }
        catch (IOException e)
        {
            Console.WriteLine($"Error while sending disconnection notification: {e.Message}");
        }
    }
}