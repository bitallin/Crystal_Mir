import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { BinaryReader, BinaryWriter } from "./binary";
import { decodeClientBody, decodeServerFrame, encodeClientBody, encodeClientFrame, encodeServerFrame } from "./codec";
import { encodeFrame, MAX_PACKET_BYTES, PacketFramer } from "./frame";
import { ClientPacketId, ServerPacketId } from "./ids";
import type { ServerPacket } from "./types";

interface SampleCase {
  name: string;
  direction: "client" | "server";
  id: number;
  hex: string;
  bytes: number;
  compressed: boolean;
  summary: Record<string, unknown> | null;
}

interface FixtureFile {
  schemaVersion: number;
  cases: SampleCase[];
}

const fixturePath = fileURLToPath(new URL("../../../tests/fixtures/protocol/cases.json", import.meta.url));
const fixture = JSON.parse(readFileSync(fixturePath, "utf8")) as FixtureFile;

function bytesOf(hex: string): Uint8Array {
  const bytes = new Uint8Array(hex.length / 2);
  for (let index = 0; index < bytes.length; index++) bytes[index] = Number.parseInt(hex.slice(index * 2, index * 2 + 2), 16);
  return bytes;
}

function hexOf(bytes: Uint8Array): string {
  return Array.from(bytes, value => value.toString(16).padStart(2, "0")).join("");
}

function frameOf(hex: string) {
  const frames = new PacketFramer().push(bytesOf(hex));
  expect(frames).toHaveLength(1);
  return frames[0]!;
}

describe("protocol fixtures generated from Shared", () => {
  for (const sample of fixture.cases) {
    it(`${sample.name} decodes and encodes byte-for-byte`, async () => {
      const frame = frameOf(sample.hex);
      expect(frame.id).toBe(sample.id);
      expect(frame.bytes.byteLength).toBe(sample.bytes);

      if (sample.direction === "client") {
        const decoded = decodeClientBody(sample.id, frame.body);
        const encoded = encodeClientBody(decoded);
        expect(encoded.id).toBe(sample.id);
        expect(hexOf(encoded.body)).toBe(hexOf(frame.body));
        return;
      }

      const decoded = await decodeServerFrame(frame);
      expect(decoded.kind).not.toBe("unknown");

      const reencoded = await encodeServerFrame(decoded);
      if (sample.compressed) {
        const roundTrip = await decodeServerFrame(frameOf(hexOf(reencoded)));
        expect(roundTrip).toEqual(decoded);
      } else {
        expect(hexOf(reencoded)).toBe(sample.hex);
      }
    });
  }
});

describe("packet framing", () => {
  it("uses the legacy unsigned-16-bit packet length limit", () => {
    expect(MAX_PACKET_BYTES).toBe(0xffff);
    expect(() => encodeFrame(1, new Uint8Array(MAX_PACKET_BYTES - 3))).toThrow(/limit/);
    expect(encodeFrame(1, new Uint8Array(MAX_PACKET_BYTES - 4))).toHaveLength(MAX_PACKET_BYTES);
  });

  it("reassembles split UTF-8 strings without decoding chunks", () => {
    const packet = encodeClientFrame({ kind: "Chat", message: "分片消息：你好，world" });
    const framer = new PacketFramer();
    expect(framer.push(packet.subarray(0, 2))).toHaveLength(0);
    expect(framer.push(packet.subarray(2, 7))).toHaveLength(0);
    const frames = framer.push(packet.subarray(7));
    expect(frames).toHaveLength(1);
    expect(decodeClientBody(frames[0]!.id, frames[0]!.body)).toEqual({ kind: "Chat", message: "分片消息：你好，world" });
  });

  it("returns consecutive packets from one WebSocket message", () => {
    const first = encodeClientFrame({ kind: "Walk", direction: 2 });
    const second = encodeClientFrame({ kind: "Run", direction: 6 });
    const merged = new Uint8Array(first.byteLength + second.byteLength);
    merged.set(first, 0);
    merged.set(second, first.byteLength);
    const frames = new PacketFramer().push(merged);
    expect(frames.map(frame => frame.id)).toEqual([ClientPacketId.Walk, ClientPacketId.Run]);
  });

  it("handles empty arrays and maximum-ish strings", async () => {
    const empty = await encodeServerFrame({ kind: "LoginSuccess", characters: [] });
    const decodedEmpty = await decodeServerFrame(frameOf(hexOf(empty)));
    expect(decodedEmpty).toEqual({ kind: "LoginSuccess", characters: [] });

    const message = "龙".repeat(20_000);
    const packet = encodeClientFrame({ kind: "Chat", message });
    const length = packet[0]! | (packet[1]! << 8);
    const decoded = decodeClientBody(ClientPacketId.Chat, packet.subarray(4, length));
    expect(decoded).toEqual({ kind: "Chat", message });
  });

  it("keeps unknown server packets opaque", async () => {
    const body = new Uint8Array([0xde, 0xad, 0xbe, 0xef]);
    const packet: ServerPacket = { kind: "unknown", id: 999, body };
    const encoded = await encodeServerFrame(packet);
    const decoded = await decodeServerFrame(frameOf(hexOf(encoded)));
    expect(decoded).toEqual({ kind: "unknown", id: 999, body });
  });

  it("rejects trailing bytes on critical packets", () => {
    expect(() => decodeClientBody(ClientPacketId.TownRevive, new Uint8Array([1]))).toThrow(/trailing bytes/);
  });
});

describe("binary primitives", () => {
  it("matches .NET BinaryWriter 7-bit string length encoding", () => {
    const writer = new BinaryWriter();
    writer.writeString("a".repeat(130));
    expect(Array.from(writer.toUint8Array().subarray(0, 2))).toEqual([0x82, 0x01]);
    const reader = new BinaryReader(writer.toUint8Array());
    expect(reader.readString()).toHaveLength(130);
    reader.expectEnd();
  });

  it("rejects malformed frames instead of looping", () => {
    expect(() => new PacketFramer().push(new Uint8Array([3, 0, 0, 0]))).toThrow(/Invalid packet length/);
  });
});

describe("fixture summaries", () => {
  it("preserves key login, map and shop semantics", async () => {
    const byName = new Map(fixture.cases.map(sample => [sample.name, sample]));
    const login = await decodeServerFrame(frameOf(byName.get("server.loginSuccess")!.hex));
    expect(login.kind).toBe("LoginSuccess");
    if (login.kind === "LoginSuccess") expect(login.characters[0]).toMatchObject({ name: "凌云", level: 42 });
    const map = await decodeServerFrame(frameOf(byName.get("server.mapInformation")!.hex));
    expect(map).toMatchObject({ kind: "MapInformation", fileName: "0", title: "比奇省", weatherParticles: 32 });
    const goods = await decodeServerFrame(frameOf(byName.get("server.npcGoods")!.hex));
    expect(goods).toMatchObject({ kind: "NPCGoods", items: [{ itemIndex: 900, count: 5 }], panelType: 0 });
  });
});
