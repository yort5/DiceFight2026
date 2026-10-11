import { useEffect, useId, useRef } from "react";
import "./TeamStartDialog.css";

export function TeamStartDialog({ messages, canProceed, onClose, onProceed }: {
  messages: string[];
  canProceed: boolean;
  onClose: () => void;
  onProceed: () => void;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  const descriptionId = useId();

  useEffect(() => {
    const opener = document.activeElement;
    const dialog = ref.current;
    dialog?.showModal();
    return () => {
      dialog?.close();
      if (opener instanceof HTMLElement && opener.isConnected) opener.focus({ preventScroll: true });
    };
  }, []);

  return <dialog ref={ref} className="team-start-dialog"
    aria-labelledby={titleId} aria-describedby={descriptionId}
    onCancel={event => { event.preventDefault(); onClose(); }}
    onClick={event => { if (event.target === event.currentTarget) onClose(); }}>
    <div className="team-start-dialog-content">
      <h2 id={titleId}>{canProceed ? "Your team is incomplete" : "Check your team"}</h2>
      <div id={descriptionId}>
        <ul>{messages.map(message => <li key={message}>{message}</li>)}</ul>
        <p>{canProceed ? "Play with this team anyway?" : "Fix these items before starting a game."}</p>
      </div>
      <div className="team-start-dialog-actions">
        <button type="button" className="team-start-dialog-back" autoFocus onClick={onClose}>
          {canProceed ? "Keep building" : "Back to team"}
        </button>
        {canProceed && <button type="button" className="team-play-button" onClick={onProceed}>Play anyway</button>}
      </div>
    </div>
  </dialog>;
}
