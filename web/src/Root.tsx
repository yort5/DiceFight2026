import App from "./App";
import { AccountPage } from "./dicekingdom/AccountPage";
import { DiceKingdomPage } from "./dicekingdom/DiceKingdomPage";
import { DiceKingdomMobilePage } from "./dicekingdom/DiceKingdomMobilePage";
import { HowToPlayPage } from "./dicekingdom/HowToPlayPage";
import { PacksPage } from "./dicekingdom/PacksPage";
import { useRoute } from "./router";
import { TeamBuilderPage } from "./TeamBuilderPage";
import { KeywordPreviewProvider } from "./KeywordPreview";
import { useTheme } from "./theme";

export function Root() {
  useTheme();
  const route = useRoute();
  return <KeywordPreviewProvider key={route}><Screen route={route} /></KeywordPreviewProvider>;
}

function Screen({ route }: { route: string }) {
  if (route === "/teambuilder") return <TeamBuilderPage />;
  if (route === "/dice-kingdom/mobile") return <DiceKingdomMobilePage />;
  if (route === "/dice-kingdom") return <DiceKingdomPage />;
  if (route === "/dice-kingdom/account") return <AccountPage />;
  if (route === "/dice-kingdom/packs") return <PacksPage />;
  if (route === "/dice-kingdom/how-to-play") return <HowToPlayPage />;
  return <App />;
}
