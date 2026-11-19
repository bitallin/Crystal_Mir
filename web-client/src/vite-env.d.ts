/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_BOOTSTRAP_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
