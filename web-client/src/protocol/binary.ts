import { ProtocolError } from "./ProtocolError";

export class BinaryReader {
  private readonly view: DataView;
  private offset = 0;

  constructor(private readonly bytes: Uint8Array) {
    this.view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  }

  get position(): number {
    return this.offset;
  }

  get remaining(): number {
    return this.bytes.byteLength - this.offset;
  }

  get length(): number {
    return this.bytes.byteLength;
  }

  readUint8(): number {
    this.ensure(1);
    return this.view.getUint8(this.offset++);
  }

  readInt8(): number {
    this.ensure(1);
    return this.view.getInt8(this.offset++);
  }

  readBoolean(): boolean {
    return this.readUint8() !== 0;
  }

  readUint16(): number {
    this.ensure(2);
    const value = this.view.getUint16(this.offset, true);
    this.offset += 2;
    return value;
  }

  readInt16(): number {
    this.ensure(2);
    const value = this.view.getInt16(this.offset, true);
    this.offset += 2;
    return value;
  }

  readUint32(): number {
    this.ensure(4);
    const value = this.view.getUint32(this.offset, true);
    this.offset += 4;
    return value;
  }

  readInt32(): number {
    this.ensure(4);
    const value = this.view.getInt32(this.offset, true);
    this.offset += 4;
    return value;
  }

  readUint64(): bigint {
    this.ensure(8);
    const value = this.view.getBigUint64(this.offset, true);
    this.offset += 8;
    return value;
  }

  readInt64(): bigint {
    this.ensure(8);
    const value = this.view.getBigInt64(this.offset, true);
    this.offset += 8;
    return value;
  }

  readSingle(): number {
    this.ensure(4);
    const value = this.view.getFloat32(this.offset, true);
    this.offset += 4;
    return value;
  }

  readBytes(length: number): Uint8Array {
    if (!Number.isSafeInteger(length) || length < 0) {
      throw new ProtocolError(`Invalid byte length ${length}`, this.offset);
    }
    this.ensure(length);
    const value = this.bytes.subarray(this.offset, this.offset + length);
    this.offset += length;
    return value;
  }

  readVarInt(): number {
    let result = 0;
    let shift = 0;
    for (let index = 0; index < 5; index++) {
      const byte = this.readUint8();
      result |= (byte & 0x7f) << shift;
      if ((byte & 0x80) === 0) return result >>> 0;
      shift += 7;
    }
    throw new ProtocolError("Malformed 7-bit encoded integer", this.offset);
  }

  readString(): string {
    const length = this.readVarInt();
    if (length > this.remaining) {
      throw new ProtocolError(`String length ${length} exceeds remaining ${this.remaining}`, this.offset);
    }
    const bytes = this.readBytes(length);
    try {
      return new TextDecoder("utf-8", { fatal: true }).decode(bytes);
    } catch {
      throw new ProtocolError("Invalid UTF-8 string", this.offset - length);
    }
  }

  readCount(label: string, maximum = 1_000_000): number {
    const count = this.readInt32();
    if (count < 0 || count > maximum) {
      throw new ProtocolError(`Invalid ${label} count ${count}`, this.offset - 4);
    }
    return count;
  }

  expectEnd(): void {
    if (this.remaining !== 0) {
      throw new ProtocolError(`Packet has ${this.remaining} trailing bytes`, this.offset);
    }
  }

  private ensure(length: number): void {
    if (length > this.remaining) {
      throw new ProtocolError(`Need ${length} bytes, only ${this.remaining} remain`, this.offset);
    }
  }
}

export class BinaryWriter {
  private buffer = new Uint8Array(64);
  private view = new DataView(this.buffer.buffer);
  private offset = 0;

  get length(): number {
    return this.offset;
  }

  writeUint8(value: number): void {
    this.ensure(1);
    this.view.setUint8(this.offset++, value & 0xff);
  }

  writeInt8(value: number): void {
    this.ensure(1);
    this.view.setInt8(this.offset++, value);
  }

  writeBoolean(value: boolean): void {
    this.writeUint8(value ? 1 : 0);
  }

  writeUint16(value: number): void {
    this.ensure(2);
    this.view.setUint16(this.offset, value, true);
    this.offset += 2;
  }

  writeInt16(value: number): void {
    this.ensure(2);
    this.view.setInt16(this.offset, value, true);
    this.offset += 2;
  }

  writeUint32(value: number): void {
    this.ensure(4);
    this.view.setUint32(this.offset, value, true);
    this.offset += 4;
  }

  writeInt32(value: number): void {
    this.ensure(4);
    this.view.setInt32(this.offset, value, true);
    this.offset += 4;
  }

  writeUint64(value: bigint): void {
    this.ensure(8);
    this.view.setBigUint64(this.offset, value, true);
    this.offset += 8;
  }

  writeInt64(value: bigint): void {
    this.ensure(8);
    this.view.setBigInt64(this.offset, value, true);
    this.offset += 8;
  }

  writeSingle(value: number): void {
    this.ensure(4);
    this.view.setFloat32(this.offset, value, true);
    this.offset += 4;
  }

  writeBytes(value: Uint8Array): void {
    this.ensure(value.byteLength);
    this.buffer.set(value, this.offset);
    this.offset += value.byteLength;
  }

  writeVarInt(value: number): void {
    if (!Number.isInteger(value) || value < 0 || value > 0x7fffffff) {
      throw new ProtocolError(`Invalid 7-bit integer ${value}`);
    }
    let remaining = value;
    while (remaining >= 0x80) {
      this.writeUint8((remaining & 0x7f) | 0x80);
      remaining >>>= 7;
    }
    this.writeUint8(remaining);
  }

  writeString(value: string): void {
    const bytes = new TextEncoder().encode(value);
    this.writeVarInt(bytes.byteLength);
    this.writeBytes(bytes);
  }

  toUint8Array(): Uint8Array {
    return this.buffer.slice(0, this.offset);
  }

  private ensure(length: number): void {
    const required = this.offset + length;
    if (required <= this.buffer.byteLength) return;
    let nextSize = this.buffer.byteLength;
    while (nextSize < required) nextSize *= 2;
    const next = new Uint8Array(nextSize);
    next.set(this.buffer.subarray(0, this.offset));
    this.buffer = next;
    this.view = new DataView(this.buffer.buffer);
  }
}
