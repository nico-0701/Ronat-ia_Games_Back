# ADR-0008 — Tempo real: o hub avisa e entrega a visão; comandos pelo REST

- **Status:** Aceito
- **Data:** 2026-10-05

## Contexto

Num jogo em que cada pessoa tem o seu celular, todos precisam ver as mudanças na hora (a vez passou, o ponto saiu, alguém entrou), e **cada um pode ver coisas diferentes**: só o mímico vê a carta. O servidor gratuito dorme e reinicia, tem uma única instância e o proxy derruba conexões ociosas; o Android suspende WebSockets em segundo plano, então conexões vão cair e voltar o tempo todo.

## Decisão

- **SignalR** em `/hubs/sessions` (já no mesmo processo da API; sem serviço extra). O Supabase Realtime foi descartado: ele transmite linhas do banco, e a regra de "quem pode ver o quê" (a visão por jogador) é do servidor C#, não de políticas de linha.
- **O hub só avisa e entrega a visão; quem age usa o REST.** Não há método de ação no hub: a autorização, a idempotência (`clientActionId`) e a concorrência (versão) têm um caminho só, o do `POST /sessions/{id}/actions`. O cliente que agiu recebe a sua visão na resposta; os demais, pelo hub.
- **A mensagem é a visão de cada assinante**, montada pelo mesmo código do REST (`SessionReader.BuildAsync`, que chama a projeção do jogo). Segredo de um jogador nunca entra na mensagem de outro, e o formato é idêntico ao do `GET`: um teste compara a mensagem empurrada com a resposta do REST.
- **Reconectar = assinar de novo.** `Subscribe` devolve o estado completo, então não existe "repor mensagens perdidas", e o cliente descarta o que for velho comparando `version`.
- **O envio sai da requisição:** um filtro nas ações de escrita enfileira o aviso; um serviço em segundo plano (`SessionBroadcaster`) junta avisos repetidos da mesma partida e envia. A resposta HTTP não espera pelo tempo real nem falha por causa dele; a camada de aplicação continua sem conhecer SignalR.
- **Autorização a cada envio:** além do `Subscribe`, o servidor reconfere, antes de cada mensagem, a participação no grupo e se o login do aparelho segue ativo (logout, "sair de todos" e suspensão valem para conexões já abertas); quem perdeu o acesso recebe `AccessRevoked` e a assinatura é encerrada. Conexões fecham quando o access token expira (`CloseOnAuthenticationExpiration`); o cliente reconecta com o token renovado.
- **Presença em memória** (um membro está online enquanto tiver uma conexão assinando a partida), e um aviso leve por grupo (`GroupSessionsChanged`) para a tela do grupo recarregar a lista.
- **Uma instância:** o registro de assinantes e a fila são em memória. É o que o plano gratuito oferece; para escalar, um *backplane* Redis e um registro compartilhado resolveriam.

## Consequências

- (+) Segurança por construção (a visão vem da projeção), um único caminho para mudar o estado e uma única forma de montar a visão; o hub é pequeno e testável (testes com cliente SignalR real, inclusive WebSocket sobre Kestrel).
- (+) Resiliente a quedas: nada depende de uma mensagem específica chegar.
- (−) Cada mudança monta uma visão por assinante (algumas consultas por pessoa); com alguns jogadores por partida é desprezível, e há espaço para compartilhar leituras se um dia pesar.
- (−) Com várias instâncias a API precisaria de backplane (avisos e presença só alcançam quem está conectado à mesma instância).
- (−) Entre uma mudança de acesso (sair do grupo, logout) e o próximo envio, a conexão aberta continua aberta (mas não recebe dados); ela cai de vez quando o token expira (no máximo 30 min).
