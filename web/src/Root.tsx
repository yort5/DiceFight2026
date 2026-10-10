import App from "./App";
import { AccountPage } from "./dicekingdom/AccountPage";
import { DiceKingdomPage } from "./dicekingdom/DiceKingdomPage";
import { DiceKingdomMobilePage } from "./dicekingdom/DiceKingdomMobilePage";
import { useRoute } from "./router";
import { TeamBuilderPage } from "./TeamBuilderPage";

export function Root() {
  const route = useRoute();
  if (route === "/teambuilder") return <TeamBuilderPage />;
  if (route === "/dice-kingdom/mobile") return <DiceKingdomMobilePage />;
  if (route === "/dice-kingdom") return <DiceKingdomPage />;
  if (route === "/dice-kingdom/account") return <AccountPage />;
  return <App />;
}
