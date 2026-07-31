import App from "./App";
import { useRoute } from "./router";
import { TeamBuilderPage } from "./TeamBuilderPage";

export function Root() {
  const route = useRoute();
  return route === "/teambuilder" ? <TeamBuilderPage /> : <App />;
}
