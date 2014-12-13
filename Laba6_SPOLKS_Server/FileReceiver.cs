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
    private const int AvailableClientsAmount = 3;
    private const int WindowSize = 5;
    private const string SyncMessage = "S";

    private int connectionFlag = 0;
    private int SelectTimeout = 20000000;

    private readonly UdpFileClient[] _udpFileReceiver;
    private readonly Dictionary<Socket, FileDetails> _fileDetails;

    private IList<Socket> _socket;
    private Dictionary<Socket, FileStream> _fileStreams;

    private IPEndPoint _remoteIpEndPoint = null;

    public FileReceiver()
    {
      _fileStreams = new Dictionary<Socket, FileStream>();
      _fileDetails = new Dictionary<Socket, FileDetails>();
      _udpFileReceiver = new UdpFileClient[AvailableClientsAmount];
      _socket = new List<Socket>();
    }

    public int Receive()
    {
      InitializeUdpClients();
      var result = ReceiveFileData();
      return result;
    }

    private void InitializeUdpClients()
    {
      for (int i = 0; i < _udpFileReceiver.Length; i++)
      {
        _udpFileReceiver[i] = new UdpFileClient(LocalPort + i);
        _udpFileReceiver[i].Client.ReceiveTimeout = _udpFileReceiver[i].Client.SendTimeout = 10000;
        _socket.Add(_udpFileReceiver[i].Client);
      }
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

      var availableToReadSockets = new List<Socket>();

      try
      {
        for (int i = 0; ; i++)
        {
          availableToReadSockets.Clear();
          availableToReadSockets.AddRange(_udpFileReceiver.Select(r => r.Client));

          Socket.Select(availableToReadSockets, null, null, SelectTimeout);

          if (availableToReadSockets.Any() == false)
          {
            return -1;
          }

          if (availableToReadSockets.Count == 1 || availableToReadSockets.Count == AvailableClientsAmount)
          {
            i = 0;
          }

          ReceiveFileDetails(availableToReadSockets);

          if (_fileDetails[availableToReadSockets[i]].FileLength > 0)
          {
            var udpClient = _udpFileReceiver.First(r => r.Client == availableToReadSockets[i]);

            if (udpClient.ActiveRemoteHost == false)
            {
              udpClient.Connect(_remoteIpEndPoint);
            }

            if (udpClient.ActiveRemoteHost)
            {
              CreateNewFile(availableToReadSockets, i);

              for (int j = 0; _fileStreams[availableToReadSockets[i]].Position < _fileDetails[availableToReadSockets[i]].FileLength && j < WindowSize; j++)
              {
                try
                {
                  var fileDataArray = udpClient.Receive(ref _remoteIpEndPoint);

                  ShowGetBytesCount();

                  _fileStreams[availableToReadSockets[i]].Write(fileDataArray, 0, fileDataArray.Length);
                  var sendBytesAmount = udpClient.Send(Encoding.UTF8.GetBytes(SyncMessage), SyncMessage.Length);
                }
                catch (SocketException e)
                {
                  if (e.SocketErrorCode == SocketError.TimedOut && connectionFlag < AvailableClientsAmount)
                  {
                    UploadFile(udpClient);
                    i = 0;
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
        return 0;
      }
      finally
      {
        CloseResources();
      }

      return 0;
    }

    private void ShowGetBytesCount()
    {
      Console.Clear();

      foreach (var file in _fileStreams)
      {
        Console.Write("{0}: ", file.Value.Name);
        Console.WriteLine(file.Value.Position);
      }
    }

    private void CreateNewFile(IList<Socket> availableToReadSockets, int i)
    {
      if (_fileStreams.ContainsKey(availableToReadSockets[i]) == false)
      {
        var dotIndex = _fileDetails[availableToReadSockets[i]].FileName.IndexOf('.');
        var fileName = _fileDetails[availableToReadSockets[i]].FileName.Substring(0, dotIndex) + availableToReadSockets[i].GetHashCode() + _fileDetails[availableToReadSockets[i]].FileName.Substring(dotIndex);
        _fileStreams[availableToReadSockets[i]] = new FileStream(fileName, FileMode.Append, FileAccess.Write);
      }
    }

    private void UploadFile(UdpFileClient udpClients)
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
    }

    private void CloseResources()
    {
      for (int i = 0; i < _socket.Count; i++)
      {
        _socket[i].Close();
        _udpFileReceiver[i].Close();
      }

      foreach (var stream in _fileStreams)
      {
        stream.Value.Close();
        stream.Value.Dispose();
      }
    }
  }
}
