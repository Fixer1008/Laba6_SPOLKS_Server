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
    private const string SyncMessage = "S";
    private const int SelectTimeout = 2000000;

    private int connectionFlag = 0;

    private Dictionary<IPEndPoint, FileDetails> _fileDetails;
    private Dictionary<IPEndPoint, FileStream> _fileStreams;

    private IList<IPEndPoint> _pointsList;
    private UdpFileClient _udpFileReceiver;

    private IPEndPoint _remoteIpEndPoint = null;

    public FileReceiver()
    {
      _fileStreams = new Dictionary<IPEndPoint, FileStream>();
      _fileDetails = new Dictionary<IPEndPoint, FileDetails>();
      _pointsList = new List<IPEndPoint>();
    }

    public int Receive()
    {
      var result = ReceiveFileData();
      return result;
    }

    private void InitializeUdpClients()
    {
      _udpFileReceiver.Client.ReceiveTimeout = _udpFileReceiver.Client.SendTimeout = 10000;
    }

    private int ReceiveFileDetails()
    {
      try
      {
        var receivedFileData = _udpFileReceiver.Receive(ref _remoteIpEndPoint);

        if (_fileDetails.ContainsKey(_remoteIpEndPoint) == false)
        {
          _pointsList.Add(_remoteIpEndPoint);

          using (MemoryStream memoryStream = new MemoryStream())
          {
            XmlSerializer serializer = new XmlSerializer(typeof(FileDetails));

            memoryStream.Write(receivedFileData, 0, receivedFileData.Length);
            memoryStream.Position = 0;

            var fileDetails = (FileDetails)serializer.Deserialize(memoryStream);
            _fileDetails.Add(_remoteIpEndPoint, fileDetails);

            Console.WriteLine(fileDetails.FileName);
            Console.WriteLine(fileDetails.FileLength);
          }
        }
        else
        {
          _fileStreams[_remoteIpEndPoint].Write(receivedFileData, 0, receivedFileData.Length);
        }

        _udpFileReceiver.Send(Encoding.UTF8.GetBytes(SyncMessage), SyncMessage.Length, _remoteIpEndPoint);
      }
      catch (Exception e)
      {
        _udpFileReceiver.Close();
        Console.WriteLine(e.Message);
        return -1;
      }

      return 0;
    }

    private int ReceiveFileData()
    {
      var availableToReadSockets = new List<Socket>();

      _udpFileReceiver = new UdpFileClient(LocalPort);
      InitializeUdpClients();

      try
      {
        for (int i = 0; ;)
        {
          availableToReadSockets.Clear();
          availableToReadSockets.Add(_udpFileReceiver.Client);

          Socket.Select(availableToReadSockets, null, null, SelectTimeout);

          if (availableToReadSockets.Any() == false)
          {
            if (_pointsList.Any() == false)
            {
              return -1;
            }

            var sendBytesAmount = _udpFileReceiver.Send(Encoding.UTF8.GetBytes(SyncMessage), SyncMessage.Length, _remoteIpEndPoint);
          }

          ReceiveFileDetails();

          if (_fileDetails[_remoteIpEndPoint].FileLength > 0)
          {
              CreateNewFile();

              try
              {
                availableToReadSockets.Clear();
                availableToReadSockets.Add(_udpFileReceiver.Client);

                Socket.Select(availableToReadSockets, null, null, SelectTimeout);

                if (availableToReadSockets.Any())
                {
                  i++;

                  var fileDataArray = _udpFileReceiver.Receive(ref _remoteIpEndPoint);

                  ShowGetBytesCount();

                  _fileStreams[_remoteIpEndPoint].Write(fileDataArray, 0, fileDataArray.Length);

                  if (_pointsList.Count > 1)
                  {
                    _udpFileReceiver.Close();

                    _udpFileReceiver = new UdpFileClient(LocalPort);
                    InitializeUdpClients();

                    var sendBytesAmount = _udpFileReceiver.Send(Encoding.UTF8.GetBytes(SyncMessage), SyncMessage.Length, _pointsList[i - 1]);
                  }

                  if (i == _pointsList.Count)
                  {
                    i = 0;
                  }
                }
              }
              catch (SocketException e)
              {
                if (e.SocketErrorCode == SocketError.TimedOut && connectionFlag < 3)
                {
                  UploadFile();
                  continue;
                }
                else
                {
                  CloseResources();
                  return -1;
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

    private void CreateNewFile()
    {
      if (_fileStreams.ContainsKey(_remoteIpEndPoint) == false)
      {
        var dotIndex = _fileDetails[_remoteIpEndPoint].FileName.IndexOf('.');
        var fileName = _fileDetails[_remoteIpEndPoint].FileName.Substring(0, dotIndex) + _remoteIpEndPoint.GetHashCode() + _fileDetails[_remoteIpEndPoint].FileName.Substring(dotIndex);
        _fileStreams[_remoteIpEndPoint] = new FileStream(fileName, FileMode.Append, FileAccess.Write);
      }
    }

    private void UploadFile()
    {
      _udpFileReceiver.Connect(_remoteIpEndPoint);

      if (_udpFileReceiver.ActiveRemoteHost)
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
      _udpFileReceiver.Close();

      foreach (var stream in _fileStreams)
      {
        stream.Value.Close();
        stream.Value.Dispose();
      }
    }
  }
}
