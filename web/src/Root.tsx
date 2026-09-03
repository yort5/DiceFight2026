import App from "./App";
import { InstinctClashPage } from "./instinct/InstinctClashPage";
import { useRoute } from "./router";
import { TeamBuilderPage } from "./TeamBuilderPage";

export function Root() {
  const route = useRoute();
  if (route === "/teambuilder") return <TeamBuilderPage />;
  if (route === "/instinct-clash") return <InstinctClashPage />;
  return <App />;
}
