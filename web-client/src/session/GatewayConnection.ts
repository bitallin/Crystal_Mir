import { decodeServerFrame, encodeClientFrame, packetIsCritical } from "../protocol/codec";
import { PacketFramer } from "../protocol/frame";
import type { ClientPacket, ServerPacket } from "../protocol/types";

export type GatewayCloseReason = "manual" | "network" | "protocol" | "upstream" | "idle";

export interface GatewayEvents {
  open: () => void;
  packet: (packet: ServerPacket) => void;
  close: (reason: GatewayCloseReason, detail: string) => void;
  error: (error: Error) => void;
}

export class GatewayConnection {
  private socket: WebSocket | null = null;
  private readonly framer = new PacketFramer();
  private decodeChain: Promise<void> = Promise.resolve();
  private intentionalClose = false;
  private opened = false;

  constructor(
    private readonly url: string,
    private readonly events: GatewayEvents
  ) {}

  get isOpen(): boolean {
    return this.socket?.readyState === WebSocket.OPEN;
  }

  connect(): Promise<void> {
    if (this.socket) this.close();
    this.intentionalClose = false;
    this.opened = false;
    this.framer.reset();
    return new Promise((resolve, reject) => {
      let settled = false;
      let socket: WebSocket;
      try {
        socket = new WebSocket(this.url, ["crystal-v1"]);
      } catch (exception) {
        reject(exception instanceof Error ? exception : new Error(String(exception)));
        return;
      }
      socket.binaryType = "arraybuffer";
      this.socket = socket;

      socket.addEventListener("open", () => {
        if (this.socket !== socket) return;
        this.opened = true;
        settled = true;
        this.events.open();
        resolve();
      });
      socket.addEventListener("message", event => {
        if (this.socket !== socket) return;
        const data = event.data;
        if (data instanceof ArrayBuffer) {
          this.receive(new Uint8Array(data));
        } else if (data instanceof Blob) {
          void data.arrayBuffer().then(buffer => this.receive(new Uint8Array(buffer)));
        } else {
          this.failProtocol("网关返回了非二进制消息");
        }
      });
      socket.addEventListener("error", () => {
        const error = new Error("WebSocket 连接错误");
        this.events.error(error);
        if (!settled) {
          settled = true;
          reject(error);
        }
      });
      socket.addEventListener("close", event => {
        if (this.socket !== socket) return;
        this.socket = null;
        if (!settled) {
          settled = true;
          reject(new Error(`WebSocket 握手失败（${event.code}）`));
        }
        const reason = this.classifyClose(event.code);
        this.events.close(reason, event.reason || (this.opened ? `连接已关闭（${event.code}）` : "无法连接区服网关"));
      });
    });
  }

  send(packet: ClientPacket): void {
    if (!this.socket || this.socket.readyState !== WebSocket.OPEN) throw new Error("WebSocket 尚未连接");
    this.socket.send(encodeClientFrame(packet));
  }

  close(): void {
    this.intentionalClose = true;
    const socket = this.socket;
    this.socket = null;
    if (socket && socket.readyState <= WebSocket.OPEN) socket.close(1000, "client closed");
  }

  private receive(chunk: Uint8Array): void {
    let frames;
    try {
      frames = this.framer.push(chunk);
    } catch (exception) {
      this.failProtocol(exception instanceof Error ? exception.message : "网关数据分帧失败");
      return;
    }
    for (const frame of frames) {
      this.decodeChain = this.decodeChain.then(async () => {
        try {
          const packet = await decodeServerFrame(frame);
          this.events.packet(packet);
        } catch (exception) {
          if (packetIsCritical(frame.id)) {
            this.failProtocol(`关键游戏包解析失败（${frame.id}）`);
          } else {
            console.warn("Ignoring malformed non-critical packet", frame.id, exception);
          }
        }
      });
    }
  }

  private failProtocol(detail: string): void {
    console.error(detail);
    const socket = this.socket;
    this.socket = null;
    if (socket && socket.readyState <= WebSocket.OPEN) socket.close(4002, "protocol error");
    this.events.close("protocol", detail);
  }

  private classifyClose(code: number): GatewayCloseReason {
    if (this.intentionalClose) return "manual";
    if (code === 1002 || code === 1003 || code === 1007 || code === 4002) return "protocol";
    if (code === 1009) return "protocol";
    if (code === 1013) return "upstream";
    return "network";
  }
}
