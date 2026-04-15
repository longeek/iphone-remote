import { createSignalingServer } from "./index.js";

const PORT = Number(process.env.PORT || 8787);
const { httpServer } = createSignalingServer();
httpServer.listen(PORT, () => {
  console.log(`Signaling listening on ws://0.0.0.0:${PORT}`);
});