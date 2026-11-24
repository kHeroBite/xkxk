# WSL(NAT)에서 Windows localhost 의 adb forward 포트(Chrome CDP)에 닿게 하는 역방향 TCP 중계기
"""사용:
  WSL 쪽 : python3 cdp_tcp_relay.py wsl  [--listen 9229] [--pool 9339]
  Windows: python.exe cdp_tcp_relay.py win [--target 9229] [--pool 9339]
원리: Windows → WSL 방향 localhost 전달만 열려 있다(NAT 모드). Windows 쪽이 WSL 의 pool 포트로 먼저 접속해
대기하고, WSL 쪽은 listen 포트에 손님이 오면 대기 중인 pool 연결 하나와 짝지어 바이트를 그대로 넘긴다.
Windows 쪽은 짝지어진(첫 바이트를 받은) 연결마다 Windows 127.0.0.1:target 에 접속해 넘기고, 새 대기 연결을 하나 더 만든다.
"""
import argparse
import asyncio


async def pump(r, w):
    try:
        while True:
            b = await r.read(65536)
            if not b:
                break
            w.write(b)
            await w.drain()
    except (ConnectionError, OSError):
        pass
    finally:
        try:
            w.close()
        except Exception:  # noqa: BLE001
            pass


async def wsl_side(a):
    pool = asyncio.Queue()

    async def on_pool(r, w):
        await pool.put((r, w))

    async def on_client(cr, cw):
        while True:
            pr, pw = await pool.get()
            if not pw.is_closing():
                break
        await asyncio.gather(pump(cr, pw), pump(pr, cw))

    s1 = await asyncio.start_server(on_pool, "127.0.0.1", a.pool)
    s2 = await asyncio.start_server(on_client, "127.0.0.1", a.listen)
    print("wsl bridge listen=%d pool=%d" % (a.listen, a.pool), flush=True)
    async with s1, s2:
        await asyncio.gather(s1.serve_forever(), s2.serve_forever())


async def win_side(a):
    async def one():
        while True:
            try:
                pr, pw = await asyncio.open_connection("127.0.0.1", a.pool)
                break
            except OSError:
                await asyncio.sleep(1)
        first = await pr.read(65536)
        asyncio.ensure_future(one())  # 다음 손님을 위한 대기 연결
        if not first:
            pw.close()
            return
        tr, tw = await asyncio.open_connection("127.0.0.1", a.target)
        tw.write(first)
        await tw.drain()
        await asyncio.gather(pump(pr, tw), pump(tr, pw))

    for _ in range(2):
        asyncio.ensure_future(one())
    print("win bridge target=%d pool=%d" % (a.target, a.pool), flush=True)
    await asyncio.Event().wait()


def main():
    p = argparse.ArgumentParser()
    p.add_argument("side", choices=["wsl", "win"])
    p.add_argument("--listen", type=int, default=9229)
    p.add_argument("--target", type=int, default=9229)
    p.add_argument("--pool", type=int, default=9339)
    a = p.parse_args()
    asyncio.run(wsl_side(a) if a.side == "wsl" else win_side(a))


if __name__ == "__main__":
    main()
