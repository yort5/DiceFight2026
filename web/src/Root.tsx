import App from "./App";
import { DiceKingdomPage } from "./dicekingdom/DiceKingdomPage";
import { useRoute } from "./router";
import { TeamBuilderPage } from "./TeamBuilderPage";

export function Root() {
  const route = useRoute();
  if (route === "/teambuilder") return <TeamBuilderPage />;
  if (route === "/dice-kingdom") return <DiceKingdomPage />;
  return <App />;
}
