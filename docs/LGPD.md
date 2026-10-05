# Privacidade e LGPD

> **Rascunho técnico, não é parecer jurídico.** Descreve o que a plataforma faz hoje com dados pessoais e aponta o que falta decidir. Antes de abrir o site para além de amigos e família, peça a revisão de quem entende da Lei 13.709/2018 (LGPD). Os pontos que dependem de decisão ou de advogado estão em [Pontos em aberto](#pontos-em-aberto).

## Resumo

- A pessoa entra **só com o telefone**; não há e-mail, senha, SMS, localização, contatos, anúncios, *cookies* de rastreio nem análise de comportamento.
- O telefone **nunca é guardado em claro**: só um HMAC dele (para reconhecer a conta) e os 4 últimos dígitos (para a pessoa se reconhecer). Quem só tem acesso ao banco não recupera o número: conferir se um número conhecido bate com o HMAC exige o segredo do servidor (`Auth:PhonePepper`).
- **Não guardamos IP nem o *user agent* inteiro** no banco: cada aparelho conectado leva só um rótulo curto ("Windows · Chrome" ou o nome que o app informar). O IP é usado só em memória, para limitar abusos e, quando o Turnstile está ligado, para a verificação anti-bot no Cloudflare. Os logs da aplicação têm só ids e códigos de erro.
- Nome e avatar aparecem para quem participa de algum grupo em comum com a pessoa. Quem só tem a **senha de um grupo** vê, ao conferi-la, o nome do grupo, quantas pessoas há e os perfis sem conta que podem ser assumidos (nome e avatar); não vê o nome de quem tem conta. O telefone não aparece para ninguém.
- A pessoa pode **baixar** seus dados (`GET /users/me/export`), **corrigir** o nome e o avatar (`PATCH /users/me`) e **excluir** a conta (`DELETE /users/me`) sozinha, pelo app.

## Quem é quem

| Papel | Quem | Observação |
|---|---|---|
| Controlador (art. 5º, VI) | **[definir]** o responsável pelo produto (pessoa física ou CNPJ) | decide as finalidades; é quem responde ao titular e à ANPD |
| Canal do titular / encarregado (art. 41) | **[definir]** um e-mail de contato | agentes de pequeno porte podem ser dispensados do encarregado (Res. CD/ANPD nº 2/2022), mas precisam oferecer um canal de atendimento |
| Operadores (art. 5º, VII) | Render (hospeda a API), Supabase (banco), Cloudflare (Pages e Turnstile, quando ligado) | tratam os dados em nome do controlador; todos nos EUA (Oregon) ou com infraestrutura global |
| Titulares | quem se cadastra; quem tem um perfil sem conta criado por um administrador de grupo | ver [Crianças e perfis sem conta](#crianças-e-perfis-sem-conta) |

## Inventário dos dados

| Dado | Onde | Para quê | Base legal sugerida (art. 7º) | Quem vê | Retenção |
|---|---|---|---|---|---|
| Telefone (HMAC + 4 últimos dígitos) | `users` | reconhecer a conta no login | execução do serviço pedido (V) | só a própria pessoa vê o final; o HMAC não é legível | até excluir a conta; ao excluir o hash vira um valor aleatório |
| Nome de exibição | `users` | aparecer nos grupos e placares | execução do serviço (V) | membros dos grupos em comum | até excluir a conta; vira "Jogador removido" |
| Avatar pronto ou foto | `users`, `avatars` | idem | execução do serviço (V); a foto é opcional e enviada por vontade da pessoa (consentimento, I) | quem receber o endereço da imagem (só membros dos grupos o recebem; o endereço não é adivinhável) | a foto é apagada ao trocar, remover ou excluir a conta |
| Aceite dos termos (data e versão) | `users` | comprovar que a pessoa aceitou | cumprimento de obrigação / exercício regular de direitos (II, VI) | só o sistema | junto com a conta anonimizada |
| Datas de criação e do último login | `users` | operação e suporte | legítimo interesse (IX) | só o sistema | idem |
| Aparelhos conectados (rótulo, datas, motivo do encerramento, hash do token) | `auth_sessions` | manter o login e permitir "sair de todos os aparelhos" | execução do serviço (V); segurança (IX) | a própria pessoa | 30 dias depois de expirar ou ser encerrado |
| Participação em grupos (papel, situação, datas) | `group_members` | controlar quem acessa o grupo | execução do serviço (V) | membros do grupo | enquanto o grupo existir; as linhas não são apagadas para o histórico continuar íntegro (a conta excluída aparece anonimizada) |
| Nome e avatar de **perfil sem conta** | `group_members` | representar quem ainda não usa o app (ex.: familiares) no placar | a definir (ver abaixo); depende de quem é a pessoa | membros do grupo e quem tiver a senha do grupo (para assumir o perfil) | enquanto o grupo existir; ao ser removido o perfil perde a foto, mas **o nome fica** |
| Partidas: jogadores, ações, eventos, pontos, resultados | `game_sessions`, `game_session_players`, `game_events`, `score_entries`, `session_results` | jogar, mostrar placar e ranking | execução do serviço (V) | membros do grupo | eventos: 60 dias depois do fim da partida; pontuação e resultados ficam (ligados a membros, não a nomes) |
| Logs da aplicação | Render | operação e diagnóstico | legítimo interesse (IX) | quem administra o Render | segundo o Render; sem telefone, nome ou token, só ids e códigos |

O **Render** e o **Cloudflare**, como qualquer hospedagem, enxergam o IP de quem acessa e podem registrá-lo nos logs de infraestrutura deles. Isso está fora do banco e do controle da aplicação; consta no aviso de privacidade.

## Direitos do titular (art. 18) e como são atendidos

| Direito | Como a plataforma atende |
|---|---|
| Confirmação e acesso (I, II) | `GET /users/me/export` devolve, num JSON, o perfil, os aparelhos (inclusive os encerrados), os grupos de que participa ou participou e o resultado de cada partida. Limite de 5 por hora por pessoa. Nunca traz o telefone completo, tokens ou dados de outras pessoas (testes automatizados conferem). |
| Correção (III) | `PATCH /users/me` (nome e avatar) e `PUT/DELETE /users/me/avatar` (foto). |
| Anonimização, bloqueio ou eliminação (IV, VI) | `DELETE /users/me` com `{ "confirmation": "EXCLUIR" }`: nome vira "Jogador removido", a foto é apagada, o hash do telefone vira um valor aleatório (o número fica livre para um novo cadastro) e todos os logins são encerrados. Quem é dono de grupo com outras pessoas precisa transferir a propriedade antes. Não dá para desfazer. |
| Portabilidade (V) | o mesmo JSON do acesso (formato aberto e estruturado). |
| Informação sobre compartilhamento (VII) | esta página e o aviso de privacidade. Os dados não são vendidos nem repassados; os operadores estão listados acima. |
| Revogação do consentimento (IX) | remover a foto ou excluir a conta. |
| Reclamação | à ANPD (gov.br/anpd), sem prejuízo do canal do controlador. |

Pedidos que não passam pelo app (ex.: quem perdeu o acesso à conta, ou um familiar que quer apagar o perfil sem conta que alguém criou) vão para o canal do controlador e são resolvidos à mão pelo administrador: ver [Operação](#operação).

## Retenção e limpeza

A limpeza é automática (ADR-0009):

| O quê | Quando some |
|---|---|
| Lobby sem mudança | cancelado após 12 h |
| Partida sem ação | cancelada após 24 h |
| Eventos de partidas encerradas | apagados após 60 dias |
| Aparelhos conectados expirados ou encerrados | apagados após 30 dias |
| Conta excluída | anonimizada na hora (a linha fica, sem dado pessoal) |
| Foto de avatar | na hora, ao trocar, remover ou excluir a conta |

Cópias de segurança do banco seguem a política do plano do Supabase contratado (**conferir**): dados excluídos podem existir nelas até o fim da retenção.

## Compartilhamento e transferência internacional

- O banco (Supabase, AWS `us-west-2`) e a API (Render, Oregon) ficam nos **Estados Unidos**. Isso é transferência internacional (art. 33): é preciso um mecanismo previsto na lei, como cláusulas contratuais-padrão (Res. CD/ANPD nº 19/2024) ou o consentimento específico. **Conferir os termos de tratamento de dados (DPA) de Render, Supabase e Cloudflare** e escolher o mecanismo.
- Não há venda, anúncios, nem ferramentas de análise ou rastreio de terceiros. O repositório é público, mas **dados pessoais nunca entram no Git** (política do projeto; há *secret scanning* e `gitleaks`).

## Crianças e perfis sem conta

- O cadastro exige um telefone próprio. Crianças e adolescentes (até 17 anos) têm dados protegidos pelo art. 14: tratamento no melhor interesse deles e, para **crianças (até 12 anos), consentimento específico de ao menos um dos pais ou responsável**.
- O caminho previsto para crianças e para quem não usa o app é o **perfil sem conta** (só nome e avatar), criado e mantido por um adulto administrador do grupo. Nenhum outro dado dessas pessoas é coletado.
- Quem cria um perfil sem conta informa nome e avatar de **outra pessoa**. O produto parte da premissa de que são familiares e amigos próximos, e que o administrador do grupo tem o consentimento deles (ou do responsável). Isso precisa constar nos termos de uso.
- O importador dos dados da família (issue #14) deve ser rodado **localmente**, com dados fora do Git, e só para quem consentiu.

## Segurança

Resumo em [SECURITY.md](SECURITY.md): telefone só como HMAC; tokens com hash; RLS no banco; HTTPS; limites de abuso; fotos reencodadas sem metadados; testes automatizados de autorização e de vazamento. Em caso de **incidente de segurança** que possa causar risco ou dano relevante, o controlador deve comunicar a ANPD e os titulares (art. 48; a Res. CD/ANPD nº 15/2024 fixa o prazo, **conferir**: em geral 3 dias úteis). Primeiros passos: trocar os segredos envolvidos, encerrar todos os logins (trocar `Jwt:SigningKey`), conter, registrar o que aconteceu.

## Operação

Para atender pedidos que o app não resolve (sempre depois de confirmar quem pede, por um canal que o administrador já conheça):

1. **Perfil sem conta a apagar:** o administrador do grupo remove o perfil (a foto some) e o mantenedor anonimiza o nome no banco (`UPDATE app.group_members SET display_name = 'Jogador removido' WHERE id = ...`).
2. **Número cadastrado por outra pessoa:** o mantenedor libera o número depois de uma verificação humana (anonimizando a conta, como na exclusão).
3. **Quem perdeu o acesso:** não há recuperação automática; o telefone é a identidade. Se a pessoa tem o mesmo número, basta entrar de novo.

## Pontos em aberto

1. **Controlador e canal de contato:** definir quem responde e publicar um e-mail.
2. **Termos de uso e aviso de privacidade:** o rascunho abaixo precisa de revisão; o app guarda a versão aceita (`Auth:TermsVersion`, hoje `2026-10`). Mudou o texto, muda a versão.
3. **Perfil sem conta removido mantém o nome** no banco (para o histórico). O titular pode pedir a eliminação; hoje isso é manual (ver [Operação](#operação)). Alternativas: anonimizar o nome automaticamente ao remover o perfil, ou oferecer "apagar o histórico dessa pessoa".
4. **Base legal dos perfis sem conta** (dados de terceiros, possivelmente crianças): decidir com a assessoria jurídica entre consentimento do responsável e as demais bases.
5. **Registros de acesso (Marco Civil, art. 15):** provedores de aplicações que exercem atividade **econômica, organizada e profissional** devem guardar os registros de acesso por 6 meses. Hoje o produto é um app de jogos para amigos, sem fim econômico, e não guarda IP; se isso mudar, reavaliar.
6. **Transferência internacional** e **backups:** ver acima.
7. **Dados de saúde, localização, biometria:** não são coletados. Se algum jogo futuro coletar (ex.: foto, voz, localização), repensar esta página antes de lançar o jogo.

## Apêndice: rascunho do aviso de privacidade (para a tela de cadastro)

> **Seus dados no Ronat-ia Games**
>
> - Para entrar você informa seu **telefone**, seu **nome** e escolhe um **avatar** (ou envia uma foto). Só isso.
> - Seu telefone **não fica guardado**: guardamos apenas uma impressão irreversível dele (para reconhecer você quando voltar) e os 4 últimos dígitos. Não enviamos SMS nem ligamos.
> - Seu nome e seu avatar aparecem para quem joga com você nos grupos de que você participa. Seu telefone não aparece para ninguém.
> - Guardamos também os grupos em que você está, as partidas que jogou e seus resultados, para mostrar o placar e o ranking do grupo.
> - Não vendemos nem repassamos seus dados, não mostramos anúncios e não rastreamos você. Usamos serviços de hospedagem (Render, Supabase e Cloudflare) nos Estados Unidos para o app funcionar.
> - Você pode, a qualquer momento, **baixar seus dados**, **corrigir** seu nome e avatar e **excluir sua conta** pelo próprio app (menu Perfil). Para qualquer outro pedido, fale com **[e-mail de contato]**.
> - Crianças só participam por um perfil criado e acompanhado por um adulto responsável do grupo.
>
> Ao criar a conta você declara ter lido este aviso e aceita os termos de uso (versão **2026-10**).
