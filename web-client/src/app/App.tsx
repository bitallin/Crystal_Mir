import { useEffect } from "preact/hooks";
import type { GameSession } from "../session/GameSession";
import { BlockingScreen, BootScreen, CharacterSelectScreen, LoginScreen } from "../ui/EntryScreens";
import { GameScreen } from "../ui/GameScreen";
import { useSession } from "../ui/useSession";

export function App({ session }: { session: GameSession }) {
  const state = useSession(session);

  useEffect(() => {
    void session.start();
    return () => session.dispose();
  }, [session]);

  if (state.phase === "boot" || (state.phase === "entering" && !state.mapAsset && !state.self)) {
    return <BootScreen state={state} />;
  }
  if (state.phase === "login") {
    return <LoginScreen state={state} onLogin={(account, password) => session.login(account, password)} />;
  }
  if (state.phase === "select") {
    return (
      <CharacterSelectScreen
        state={state}
        onSelect={index => session.selectCharacter(index)}
        onCreate={(name, gender, classId) => session.createCharacter(name, gender, classId)}
        onDelete={index => session.deleteCharacter(index)}
      />
    );
  }
  if (state.phase === "blocked") {
    return <BlockingScreen state={state} onRetry={() => void session.retry()} onReconnectAssets={() => session.retryMap()} />;
  }
  if (state.phase === "error") {
    return <BlockingScreen state={state} onRetry={() => void session.retry()} />;
  }
  if (state.phase === "game" || state.phase === "entering" || state.phase === "reconnecting") {
    return <GameScreen session={session} state={state} />;
  }
  return <BootScreen state={state} />;
}
