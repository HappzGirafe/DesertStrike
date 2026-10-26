// The browser version's link to an online room (see WebLink.cs): a WebSocket to the online server, which passes each
// binary message on to the room's game host as one UDP packet (online-server/webrooms.py). Messages that arrive wait
// in a queue until the game takes them, a frame at a time.
mergeInto(LibraryManager.library, {
  WebLinkOpen: function (urlPointer) {
    var url = UTF8ToString(urlPointer);
    if (!Module.webLinks) { Module.webLinks = {}; Module.webLinkNext = 1; }
    var id = Module.webLinkNext++;
    var link = { socket: null, queue: [], state: 0 };   // 0 connecting, 1 open, 2 closed
    Module.webLinks[id] = link;
    try {
      var socket = new WebSocket(url);
      socket.binaryType = "arraybuffer";
      socket.onopen = function () { link.state = 1; };
      socket.onclose = function () { link.state = 2; };
      socket.onerror = function () { link.state = 2; };
      socket.onmessage = function (e) {
        if (e.data instanceof ArrayBuffer && link.queue.length < 512) link.queue.push(new Uint8Array(e.data));
      };
      link.socket = socket;
    } catch (e) {
      link.state = 2;
    }
    return id;
  },

  WebLinkState: function (id) {
    var link = Module.webLinks && Module.webLinks[id];
    return link ? link.state : 2;
  },

  WebLinkSend: function (id, bufferPointer, length) {
    var link = Module.webLinks && Module.webLinks[id];
    if (!link || link.state !== 1) return 0;
    link.socket.send(HEAPU8.slice(bufferPointer, bufferPointer + length));
    return 1;
  },

  // Copies the next message into the buffer and returns its length; 0 when there is none (-1: too big, dropped).
  WebLinkReceive: function (id, bufferPointer, capacity) {
    var link = Module.webLinks && Module.webLinks[id];
    if (!link || link.queue.length === 0) return 0;
    var data = link.queue.shift();
    if (data.length > capacity) return -1;
    HEAPU8.set(data, bufferPointer);
    return data.length;
  },

  WebLinkClose: function (id) {
    var link = Module.webLinks && Module.webLinks[id];
    if (!link) return;
    try { if (link.socket) link.socket.close(); } catch (e) {}
    delete Module.webLinks[id];
  }
});
