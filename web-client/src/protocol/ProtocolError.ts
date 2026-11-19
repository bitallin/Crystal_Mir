export class ProtocolError extends Error {
  constructor(message: string, readonly offset?: number) {
    super(message);
    this.name = "ProtocolError";
  }
}

export class FrameBufferError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "FrameBufferError";
  }
}
