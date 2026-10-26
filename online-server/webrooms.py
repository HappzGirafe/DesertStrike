"""Online rooms for the browser version (WebGL): a browser cannot send UDP, so its game connects here with a
WebSocket, and each binary message goes on as one UDP packet to the room's game host on this computer (and each packet
back comes out as one message). The game host sees a player at 127.0.0.1:<port>, just like one coming through playit,
and checks the join ticket in their Hello as always: the link itself lets nobody in.

    WS /rooms/ws/{slot_id}    binary messages both ways, one game packet each

Only from the website (the same addresses as REVIEW_ORIGINS) or from no browser at all; at most a few links per
address and LINKS_MAX in all, each limited to the packet size and rate a game uses.
"""
import asyncio

from fastapi import WebSocket

MAX_PACKET = 1400          # NetSession.MaxPacket
LINKS_MAX = 60             # three rooms of ten players, twice over
LINKS_PER_ADDRESS = 4
PACKETS_PER_SECOND = 240   # a player sends ~30 inputs, a few shots and Hellos a second


def install(app, s, origins: set):
    links = {"total": 0}
    per_address = {}

    @app.websocket("/rooms/ws/{slot_id}")
    async def room_link(ws: WebSocket, slot_id: str):
        slot = next((x for x in s.CONFIG.slots if x["id"] == slot_id), None)
        address = ws.headers.get("cf-connecting-ip") or (ws.client.host if ws.client else "?")
        origin = ws.headers.get("origin", "").rstrip("/")
        if (slot is None or (origin and origin not in origins) or links["total"] >= LINKS_MAX
                or per_address.get(address, 0) >= LINKS_PER_ADDRESS):
            await ws.close(code=1008)
            return
        await ws.accept()
        links["total"] += 1
        per_address[address] = per_address.get(address, 0) + 1
        loop = asyncio.get_running_loop()
        back = asyncio.Queue(maxsize=256)

        class FromHost(asyncio.DatagramProtocol):
            def datagram_received(self, data, _):
                try:
                    back.put_nowait(data)
                except asyncio.QueueFull:
                    pass   # the browser is too slow to take them: newer snapshots follow anyway

        transport, _ = await loop.create_datagram_endpoint(FromHost, remote_addr=("127.0.0.1", int(slot["local_udp_port"])))

        async def to_browser():
            while True:
                await ws.send_bytes(await back.get())

        sender = asyncio.create_task(to_browser())
        window, sent = loop.time(), 0
        try:
            while True:
                message = await ws.receive()
                if message["type"] == "websocket.disconnect":
                    break
                data = message.get("bytes")
                if not data or len(data) > MAX_PACKET:
                    continue
                now = loop.time()
                if now - window >= 1.0:
                    window, sent = now, 0
                sent += 1
                if sent <= PACKETS_PER_SECOND:
                    transport.sendto(data)
        except Exception:
            pass   # the browser went away
        finally:
            sender.cancel()
            transport.close()
            links["total"] -= 1
            per_address[address] -= 1
            if per_address[address] <= 0:
                del per_address[address]
            try:
                await ws.close()
            except Exception:
                pass

    return {"links": lambda: links["total"]}
