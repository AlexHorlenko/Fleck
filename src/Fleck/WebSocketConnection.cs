using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

namespace Fleck
{
    public class WebSocketConnection : IWebSocketConnection
  {
    public WebSocketConnection(ISocket socket, Action<IWebSocketConnection> initialize, Func<byte[], WebSocketHttpRequest> parseRequest, Func<WebSocketHttpRequest, IHandler> handlerFactory, Func<IEnumerable<string>, string> negotiateSubProtocol, Func<IEnumerable<string>, WebSocketExtension> negotiateExtension = null)
    {
      Socket = socket;
      OnOpen = () => { };
      OnClose = () => { };
      OnMessage = x => { };
      OnBinary = x => { };
      OnPing = x => SendPong(x);
      OnPong = x => { };
      OnError = x => { };
      _initialize = initialize;
      _handlerFactory = handlerFactory;
      _parseRequest = parseRequest;
      _negotiateSubProtocol = negotiateSubProtocol;
      _negotiateExtension = negotiateExtension;
    }

    //WHY this exists?
    //public WebSocketConnection(ISocket clientSocket, Action<IWebSocketConnection> _config, Func<byte[], WebSocketHttpRequest> func1, Func<WebSocketHttpRequest, IHandler> func2, Func<IEnumerable<string>, string> func3, Func<IEnumerable<string>, string> func4)
    //{
    //     TODO: Complete member initialization
    //    this.clientSocket = clientSocket;
    //    this._config = _config;
    //    this.func1 = func1;
    //    this.func2 = func2;
    //    this.func3 = func3;
    //    this.func4 = func4;
    //}

    public ISocket Socket { get; set; }

    private readonly Action<IWebSocketConnection> _initialize;
    private readonly Func<WebSocketHttpRequest, IHandler> _handlerFactory;
    private readonly Func<IEnumerable<string>, string> _negotiateSubProtocol;
    private readonly Func<IEnumerable<string>, WebSocketExtension> _negotiateExtension;
    readonly Func<byte[], WebSocketHttpRequest> _parseRequest;

    public IHandler Handler { get; set; }
    public Handlers.ComposableHanderWithExtensions HandlerWithExtension { get; set; }

    private bool _closing;
    private bool _closed;
    private const int ReadSize = 1024 * 4;
    private ISocket clientSocket;
    private Action<IWebSocketConnection> _config;
    private Func<byte[], WebSocketHttpRequest> func1;
    private Func<WebSocketHttpRequest, IHandler> func2;
    private Func<IEnumerable<string>, string> func3;
    private Func<IEnumerable<string>, string> func4;

    public Action OnOpen { get; set; }

    public Action OnClose { get; set; }

    public Action<string> OnMessage { get; set; }

    public Action<byte[]> OnBinary { get; set; }

    public Action<byte[]> OnPing { get; set; }

    public Action<byte[]> OnPong { get; set; }

    public Action<Exception> OnError { get; set; }

    public WebSocketExtension Extension { get; set; }

    public IWebSocketConnectionInfo ConnectionInfo { get; private set; }

    public bool IsAvailable {
      get { return !_closing && !_closed && Socket.Connected; }
    }

    public Task Send(string message)
    {
        return (Extension == null) ? Send(message, Handler.FrameText) : Send(message, HandlerWithExtension.FrameText);
    }

    public Task Send(byte[] message)
    {
        return (Extension == null) ? Send(message, Handler.FrameBinary) : Send(message, HandlerWithExtension.FrameBinary); ;
    }

    public Task SendPing(byte[] message)
    {
        return Send(message, Handler.FramePing);
    }

    public Task SendPong(byte[] message)
    {
        return Send(message, Handler.FramePong);
    }

    private Task Send<T>(T message, Func<T, WebSocketExtension, byte[]> createFrame)
    {
        if (HandlerWithExtension == null)
            throw new InvalidOperationException("Cannot send before handshake");
        if (!IsAvailable)
        {
            const string errorMessage = "Data sent while closing or after close. Ignoring.";
            FleckLog.Warn(errorMessage);

            var taskForException = new TaskCompletionSource<object>();
            taskForException.SetException(new ConnectionNotAvailableException(errorMessage));
            return taskForException.Task;
        }

        var bytes = createFrame(message, Extension);
        return SendBytes(bytes);
    }

    private Task Send<T>(T message, Func<T, byte[]> createFrame)
    {
      if (Handler == null)
        throw new InvalidOperationException("Cannot send before handshake");

      if (!IsAvailable)
      {
          const string errorMessage = "Data sent while closing or after close. Ignoring.";
          FleckLog.Warn(errorMessage);
          
          var taskForException = new TaskCompletionSource<object>();
          taskForException.SetException(new ConnectionNotAvailableException(errorMessage));
          return taskForException.Task;
      }

      var bytes = createFrame(message);
      return SendBytes(bytes);
    }

    public void StartReceiving()
    {
      var data = new List<byte>(ReadSize);
      var buffer = new byte[ReadSize];
      Read(data, buffer);
    }

    public void Close()
    {
      Close(WebSocketStatusCodes.NormalClosure);
    }

    public void Close(int code)
    {
      if (!IsAvailable)
        return;

      _closing = true;

      if (Handler == null) {
        CloseSocket();
        return;
      }

      var bytes = Handler.FrameClose(code);
      if (bytes.Length == 0)
        CloseSocket();
      else
        SendBytes(bytes, CloseSocket);
    }

    public void CreateHandler(IEnumerable<byte> data)
    {
      var request = _parseRequest(data.ToArray());
      if (request == null)
        return;
      Handler = _handlerFactory(request);
      if (Handler == null)
        return;
      var subProtocol = _negotiateSubProtocol(request.SubProtocols);
      string extensionName = null;
      if (request.Extensions.Count() > 0)
      {
          Extension = _negotiateExtension(request.Extensions);
          if (Extension != null)
          {
              extensionName = Extension.GetWebSocketExtensionRepresentation();
              HandlerWithExtension = Handlers.Hybi13HandlerWithExtension.Create(request, (s) => OnMessage(s), OnClose,(b) => OnBinary(b),(b) => OnPing(b),(b) => OnPong(b));
          }     
      }
      
      ConnectionInfo = WebSocketConnectionInfo.Create(request, Socket.RemoteIpAddress, Socket.RemotePort, subProtocol, extensionName);

      _initialize(this);
      byte[] handshake;
      if (HandlerWithExtension != null)
          handshake = HandlerWithExtension.CreateHandshake(subProtocol, extensionName);
      else
          handshake = Handler.CreateHandshake(subProtocol);
      SendBytes(handshake, OnOpen);
    }

    private void Read(List<byte> data, byte[] buffer)
    {
      if (!IsAvailable)
        return;

      Socket.Receive(buffer, r =>
      {
        if (r <= 0) {
          FleckLog.Debug("0 bytes read. Closing.");
          CloseSocket();
          return;
        }
        FleckLog.Debug(r + " bytes read");
        var readBytes = buffer.Take(r);
        if (Handler != null) {
            if (Extension != null)
                HandlerWithExtension.Receive(readBytes, Extension);
            else
                Handler.Receive(readBytes);
        } else {
          data.AddRange(readBytes);
          CreateHandler(data);
        }

        Read(data, buffer);
      },
      HandleReadError);
    }

    private void HandleReadError(Exception e)
    {
      if (e is AggregateException) {
        var agg = e as AggregateException;
        HandleReadError(agg.InnerException);
        return;
      }

      if (e is ObjectDisposedException) {
        FleckLog.Debug("Swallowing ObjectDisposedException", e);
        return;
      }

      OnError(e);
            
      if (e is HandshakeException) {
        FleckLog.Debug("Error while reading", e);
      } else if (e is WebSocketException) {
        FleckLog.Debug("Error while reading", e);
        Close(((WebSocketException)e).StatusCode);
      } else if (e is SubProtocolNegotiationFailureException) {
        FleckLog.Debug(e.Message);
        Close(WebSocketStatusCodes.ProtocolError);
      } else if (e is IOException) {
        FleckLog.Debug("Error while reading", e);
        Close(WebSocketStatusCodes.AbnormalClosure);
      } else {
        FleckLog.Error("Application Error", e);
        Close(WebSocketStatusCodes.InternalServerError);
      }
    }

    private Task SendBytes(byte[] bytes, Action callback = null)
    {
      return Socket.Send(bytes, () =>
      {
        FleckLog.Debug("Sent " + bytes.Length + " bytes");
        if (callback != null)
          callback();
      },
                        e =>
      {
        if (e is IOException)
          FleckLog.Debug("Failed to send. Disconnecting.", e);
        else
          FleckLog.Info("Failed to send. Disconnecting.", e);
        CloseSocket();
      });
    }

    private void CloseSocket()
    {
      _closing = true;
      OnClose();
      _closed = true;
      Socket.Close();
      Socket.Dispose();
      _closing = false;
    }

  }
}
