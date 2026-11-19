import { useEffect, useState } from "preact/hooks";
import type { GameSession, SessionState } from "../session/GameSession";

export function useSession(session: GameSession): SessionState {
  const [state, setState] = useState<SessionState>(session.current);
  useEffect(() => session.subscribe(() => setState(session.current)), [session]);
  return state;
}
