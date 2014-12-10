using System;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Xml.Serialization;
using System.Diagnostics;
using System.Collections.Generic;

namespace Laba6_SPOLKS_Server
{
  public class FileReceiver
  {
    private const int Size = 8192;
    private const int LocalPort = 11000;
    private const int N = 3;
    private const string SyncMessage = "SYNC";

    private int connectionFlag = 0;
    private int SelectTimeout = 10000000;

    private readonly UdpFileClient[] _udpFileReceiver;
    private readonly Dictionary<Socket, FileDetails> _fileDetails;

    private Dictionary<Socket, FileStream> _fileStream;
    private IPEndPoint _remoteIpEndPoint = null;
    private EndPoint _remoteEndPoint;
    private IList<Socket> _socket;

    public FileReceiver()
    {
      _fileStream = new Dictionary<Socket, FileStream>();
      _fileDetails = new Dictionary<Socket, FileDetails>();
      _udpFileReceiver = new UdpFileClient[N];
      _socket = new List<Socket>();
    }

    void InitializeUdpClients()
    {
      for (int i = 0; i < _udpFileReceiver.Length; i++)
      {
        _udpFileReceiver[i] = new UdpFileClient(LocalPort + i);
        _udpFileReceiver[i].Client.ReceiveTimeout = _udpFileReceiver[i].Client.SendTimeout = 10000;
        _socket.Add(_udpFileReceiver[i].Client);
      }
    }

    public int Receive()
    {
      InitializeUdpClients();
      var result = ReceiveFileData();
      return result;
    }

    private int ReceiveFileDetails(IList<Socket> checkReadSocket)
    {
      foreach (var socket in checkReadSocket)
      {
        if (_fileDetails.ContainsKey(socket) == false)
        {
          MemoryStream memoryStream = new MemoryStream();

          try
          {
            var udpClient = _udpFileReceiver.First(s => s.Client == socket);
            var receivedFileInfo = udpClient.Receive(ref _remoteIpEndPoint);

            XmlSerializer serializer = new XmlSerializer(typeof(FileDetails));

            memoryStream.Write(receivedFileInfo, 0, receivedFileInfo.Length);
            memoryStream.Position = 0;

            var fileDetails = (FileDetails)serializer.Deserialize(memoryStream);
            _fileDetails.Add(socket, fileDetails);

            Console.WriteLine(fileDetails.FileName);
            Console.WriteLine(fileDetails.FileLength);
          }
          catch (Exception e)
          {
            for (int i = 0; i < _socket.Count; i++)
            {
              _socket[i].Close();
            }

            memoryStream.Dispose();
            Console.WriteLine(e.Message);
            return -1;
          }

          memoryStream.Dispose();
        }
      }

      return 0;
    }

    private int ReceiveFileData()
    {
      int filePointer = 0;
      bool allClientsEndTransmit = false;

      var checkReadSocket = new List<Socket>();

      try
      {
        for (int i = 0; allClientsEndTransmit == false; i++)
        {
          checkReadSocket.Clear();
          checkReadSocket.AddRange(_udpFileReceiver.Select(r => r.Client));

          Socket.Select(checkReadSocket, null, null, SelectTimeout);

          if (checkReadSocket.Any() == false)
          {
            return -1;
          }

          if (checkReadSocket.Count == 1 || checkReadSocket.Count == N)
          {
            i = 0;
          }
          else
          {
            Console.WriteLine("2nd connect!");
          }

          SelectTimeout = 100000;

          ReceiveFileDetails(checkReadSocket);

          if (_fileDetails[checkReadSocket[i]].FileLength > 0)
          {
            var udpClients = _udpFileReceiver.First(r => r.Client == checkReadSocket[i]);

            if (udpClients.ActiveRemoteHost == false)
            {
              udpClients.Connect(_remoteIpEndPoint);
            }

            if (udpClients.ActiveRemoteHost)
            {
              if (_fileStream.ContainsKey(checkReadSocket[i]) == false)
              {
                var dotIndex = _fileDetails[checkReadSocket[i]].FileName.IndexOf('.');
                var fileName = _fileDetails[checkReadSocket[i]].FileName.Substring(0, dotIndex) + checkReadSocket[i].GetHashCode() + _fileDetails[checkReadSocket[i]].FileName.Substring(dotIndex);
                _fileStream[checkReadSocket[i]] = new FileStream(fileName, FileMode.Append, FileAccess.Write);
              }

              for (int j = 0; _fileStream[checkReadSocket[i]].Position < _fileDetails[checkReadSocket[i]].FileLength && j < 5; j++)
              {
                try
                {
                  var fileDataArray = udpClients.Receive(ref _remoteIpEndPoint);

                  filePointer += fileDataArray.Length;
                  Console.Clear();
                  Console.WriteLine(filePointer);

                  _fileStream[checkReadSocket[i]].Write(fileDataArray, 0, fileDataArray.Length);

                  var sendBytesAmount = udpClients.Send(Encoding.UTF8.GetBytes(SyncMessage), SyncMessage.Length);
                }
                catch (SocketException e)
                {
                  if (e.SocketErrorCode == SocketError.TimedOut && connectionFlag < 3)
                  {
                    udpClients.Connect(_remoteIpEndPoint);

                    if (udpClients.ActiveRemoteHost)
                    {
                      connectionFlag = 0;
                    }
                    else
                    {
                      connectionFlag++;
                    }

                    continue;
                  }
                  else
                  {
                    CloseResources();
                    return -1;
                  }
                }
              }
            }
          }
          else
          {
            CloseResources();
            return -1;
          }
        }
      }
      catch (Exception e)
      {
        Console.WriteLine(e.Message);
        return -1;
      }
      finally
      {
        CloseResources();
      }

      return 0;
    }

    private void CloseResources()
    {
      for (int i = 0; i < _socket.Count; i++)
      {
        _socket[i].Close();
        _udpFileReceiver[i].Close();
      }

      foreach (var stream in _fileStream)
      {
        stream.Value.Close();
        stream.Value.Dispose();
      }
    }
  }
}
