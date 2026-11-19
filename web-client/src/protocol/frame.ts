import { FrameBufferError, ProtocolError } from "./ProtocolError";

export const MAX_PACKET_BYTES = 0xffff;

export interface RawPacketFrame {
  id: number;
  body: Uint8Array;
  bytes: Uint8Array;
}

export function encodeFrame(id: number, body: Uint8Array): Uint8Array {
  const length = body.byteLength + 4;
  if (length > MAX_PACKET_BYTES) {
    throw new ProtocolError(`Packet ${id} is ${length} bytes; limit is ${MAX_PACKET_BYTES}`);
  }
  if (id < -32768 || id > 32767) throw new ProtocolError(`Invalid packet id ${id}`);
  const bytes = new Uint8Array(length);
  const view = new DataView(bytes.buffer);
  view.setUint16(0, length, true);
  view.setInt16(2, id, true);
  bytes.set(body, 4);
  return bytes;
}

export class PacketFramer {
  private pending = new Uint8Array(0);

  push(chunk: Uint8Array): RawPacketFrame[] {
    if (chunk.byteLength === 0) return [];
    const combined = new Uint8Array(this.pending.byteLength + chunk.byteLength);
    combined.set(this.pending, 0);
    combined.set(chunk, this.pending.byteLength);
    this.pending = combined;

    const frames: RawPacketFrame[] = [];
    let offset = 0;
    while (this.pending.byteLength - offset >= 4) {
      const view = new DataView(this.pending.buffer, this.pending.byteOffset + offset, this.pending.byteLength - offset);
      const length = view.getUint16(0, true);
      if (length < 4) {
        this.pending = new Uint8Array(0);
        throw new FrameBufferError(`Invalid packet length ${length}`);
      }
      if (length > MAX_PACKET_BYTES) {
        this.pending = new Uint8Array(0);
        throw new FrameBufferError(`Packet length ${length} exceeds ${MAX_PACKET_BYTES}`);
      }
      if (this.pending.byteLength - offset < length) break;

      const id = view.getInt16(2, true);
      const bytes = this.pending.slice(offset, offset + length);
      frames.push({ id, body: bytes.subarray(4), bytes });
      offset += length;
    }

    if (offset > 0) this.pending = this.pending.slice(offset);
    return frames;
  }

  reset(): void {
    this.pending = new Uint8Array(0);
  }
}
