# Segurança

Resumo do modelo de ameaças, dos **riscos aceitos** (de propósito) e dos controles implementados. Quando uma decisão muda o que protegemos, ela ganha um ADR.

## O que protegemos

| Ativo | Como |
|---|---|
| Número de telefone das pessoas | nunca em claro: só HMAC-SHA256 com segredo do servidor + 4 últimos dígitos; nunca exibido a outros usuários |
| Sessão (quem é quem) | JWT curto + refresh token rotativo, verificado a cada requisição (ADR-0004) |
| Dados de grupos e partidas | só membros acessam; a regra é do servidor, não do cliente |
| Placar e regras dos jogos | calculados no servidor; o cliente envia ações, nunca pontuação |
| Informação secreta de um jogo (ex.: a carta do mímico) | o servidor devolve a cada jogador só o que ele pode ver |
| Segredos de infraestrutura | variáveis de ambiente / `dotnet user-secrets`; nunca no Git |

## Riscos aceitos

> Decisão do dono do produto: o site é público, mas o produto é **um app de jogos para amigos**. O telefone é só um registro único. **Sem SMS e sem senha** (ADR-0003).

| Risco | Impacto | Mitigações |
|---|---|---|
| **Quem souber o telefone de alguém entra na conta dessa pessoa** (o número não é verificado) | limitado a nome, avatar e grupos da conta; o acesso a um grupo ainda exige a *senha do grupo* | limite de requisições por IP; Turnstile (quando configurado); telefone nunca exposto; sessões listáveis e revogáveis ("sair de todos os aparelhos"); a evolução prevista é acrescentar verificação (WhatsApp/SMS) ou PIN atrás de configuração |
| **Cadastro do número de outra pessoa** antes dela | a pessoa não consegue criar a própria conta | o administrador pode liberar o número depois de uma verificação humana (fora do sistema) |
| **Enumeração de contas** (o login responde 404 para número desconhecido, para o app saber que deve mostrar o cadastro) | descobrir quais números têm conta | limite por IP, Turnstile, `Registration:Mode=closed` como válvula de escape |
| **A senha do grupo é compartilhada**: qualquer membro pode repassá-la, e ela fica guardada em claro (todo membro precisa vê-la de novo) | entra no grupo quem tiver a senha: vê nomes, avatares e (nas próximas fases) o histórico do grupo | 30^8 combinações com limite de 10 tentativas/min por pessoa; dono/admin **desativam** ou **trocam** a senha (a antiga morre na hora) e removem quem não devia estar; erro idêntico para senha errada, desativada e de grupo excluído (ADR-0006) |
| **Reivindicar um perfil sem conta não passa por aprovação**: quem tem a senha pode assumir o perfil de qualquer pessoa sem conta do grupo | alguém "vira" a vovó no placar | num grupo de amigos a confiança já está na senha; o dono remove a pessoa e recria o perfil; se virar problema, o modelo comporta uma aprovação do dono |
| **Veredito autodeclarado nos jogos presenciais** (o servidor não vê a mímica) | pontuação "por honra" | o servidor garante autorização, ordem, tempo e contabilidade; ranking é por diversão |

## Controles implementados

### Identidade e sessões
- **JWT de acesso (HS256, 30 min):** algoritmo fixo na validação (recusa `alg: none`), emissor e audiência validados, relógio injetado, só leva `sub` (conta), `sid` (sessão) e, para admins, `role`. Sem nome nem telefone.
- **Refresh token:** 256 bits aleatórios, guardado só como SHA-256, **rotativo** a cada uso, validade deslizante de 90 dias, uma sessão por aparelho (máximo de 10; o menos usado é desconectado).
- **Detecção de reutilização:** reapresentar um refresh token já trocado revoga a sessão inteira, exceto numa janela de 15 s (nova tentativa legítima de quem perdeu a resposta por rede instável).
- **Revogação imediata:** a cada requisição autenticada o servidor confere que a sessão não foi revogada e que a conta está ativa (cache de 10 s). Logout, "sair de todos os aparelhos" e suspensão valem na hora, sem esperar o JWT expirar.
- **A identidade vem do token**, nunca de um telefone, id ou cabeçalho enviado pelo cliente. Todo controller exige login por padrão (`RequireAuthorization()`); o público precisa de `[AllowAnonymous]` explícito.
- **Concorrência:** `version` na sessão (otimista) impede duas renovações simultâneas de gravarem as duas.

### Grupos
- **Autorização no servidor** (`GroupPermissions`): dono, administrador e membro (ADR-0006); o cliente só esconde botões. A identidade de quem age vem do token, e o papel é lido do banco a cada chamada.
- **Quem não é membro recebe `404`, nunca `403`**, para todas as rotas de um grupo: não se descobre que um grupo existe. Há testes de "pessoa de fora" para cada operação.
- **Senha do grupo:** 8 caracteres sorteados com `RandomNumberGenerator` (sem viés) de um alfabeto de 30 símbolos; limite de 10 entradas/conferências por minuto **por pessoa** (e o teto por IP); `group.invalid_code` idêntico para senha errada, desativada e de grupo excluído; entrada/conferência por `POST` (a senha não vai em URL nem em log de acesso).
- **Invariantes no banco**, além do código: um dono ativo por grupo (índice único parcial), uma linha por pessoa e grupo, perfil sem conta consistente (nome + exatamente um avatar), dono sempre com conta, formato da senha. Testes tentam violar cada uma diretamente no SQL.
- **Concorrência:** a reivindicação de um perfil usa um token de versão (a segunda de duas simultâneas recebe `409`); a transferência de propriedade roda numa transação (rebaixa, depois promove); a entrada é idempotente e um duplo clique não duplica o membro.
- **Limites:** 20 grupos por pessoa, 100 membros por grupo, 10 grupos criados por hora; as fotos de perfis sem conta passam pelo mesmo processamento e limites das fotos de conta.

### Partidas e jogos
- **O servidor é a fonte da verdade** (ADR-0007): o cliente envia *ações*, nunca pontuação; cada ação é validada pelo módulo do jogo (quem pode, em que fase, dentro do prazo) e os pontos são calculados no servidor.
- **Segredo só pela projeção:** o estado do jogo (a carta, o papel de cada um) nunca é devolvido; cada pessoa recebe só o que o módulo projeta para ela, e quem só assiste recebe a visão pública. A trilha de eventos guarda **fatos públicos** (o conteúdo das ações, que pode ser um palpite secreto, não é gravado). Testes de vazamento conferem que a palavra secreta não aparece na partida, na trilha nem na listagem para quem não pode vê-la.
- **Acesso:** só membros ativos do grupo; quem não é membro recebe `404`, nunca `403`. Gerenciar (lobby, começar, cancelar, encerrar, revanche) é do anfitrião ou de administradores do grupo; agir exige ser jogador ou gerente.
- **Concorrência e idempotência garantidas pelo banco:** `version` como token otimista, `UNIQUE (session_id, seq)` e `UNIQUE (session_id, client_action_id)`; uma ação é uma única transação (estado + eventos + pontos + resultado). Testes disparam ações simultâneas (incluindo o mesmo `clientActionId` duas vezes) e conferem que nada é perdido nem duplicado.
- **Tempo:** prazos são dados avaliados pelo servidor com o relógio dele; o cliente não consegue "estender" um turno.
- **Limites contra abuso:** 5 partidas abertas por grupo, 30 criações/hora e 240 ações/minuto por pessoa; a leitura da trilha é paginada.
- **Módulos de jogo são código confiável** (revisado como o resto do repositório), mas isolados por construção: dependem só de `Abstractions` (sem banco, rede nem relógio) e os erros que lançam viram respostas 4xx com código estável, sem vazar pilha.

### Tempo real (SignalR)
- **O hub não executa ações**: só avisa e entrega a visão (ADR-0008). Mudar o estado continua passando pelo REST, com sua autorização, idempotência e concorrência.
- **A mensagem é a visão de cada assinante**, montada pelo mesmo código do REST (a projeção do jogo): segredo de um jogador nunca vai na mensagem de outro. Testes com cliente SignalR real conferem que a palavra secreta não aparece nas mensagens de quem não pode vê-la, e que a mensagem é idêntica à resposta do REST.
- **Autenticação:** o token vai na query (`access_token`) **só em `/hubs`** (o WebSocket não envia cabeçalho); em qualquer outra rota é ignorado. Os logs não registram query strings (os de requisição guardam só o caminho; os do framework ficam em `Warning`). A negociação e a conexão passam pela mesma validação do REST, inclusive a sessão de login revogada.
- **Autorização contínua:** o `Subscribe` exige ser membro do grupo; antes de **cada** envio o servidor reconfere a participação no grupo e se o login daquele aparelho segue ativo. Quem saiu do grupo ou deslogou recebe `AccessRevoked` e nada mais; os outros aparelhos da mesma pessoa não são afetados. A conexão é encerrada pelo servidor quando o token expira (30 min).
- **Limites:** 20 assinaturas por conexão, mensagens de entrada de até 16 KB, **120 chamadas por minuto por conexão** (o limitador de requisições do ASP.NET não enxerga mensagens de uma conexão já aberta; passou do limite, a chamada falha com `rate_limit.exceeded` até a janela virar) e limite por IP também na negociação. Presença e assinantes ficam em memória (uma instância).

### Segredos e configuração
- `Auth:PhonePepper` e `Jwt:SigningKey` são validados **na subida**: ausentes, curtos (< 32 bytes) ou iguais aos valores públicos de desenvolvimento fora de Development → a API **não inicia**.
- `appsettings.json` não traz segredos; segredos reais vêm de variáveis de ambiente (Render) ou `dotnet user-secrets` (desenvolvimento).
- **`Auth:PhonePepper` é insubstituível:** trocá-lo invalida todos os logins (os telefones só existem como HMAC). Guarde uma cópia em gerenciador de senhas.
- Rotação da chave do JWT: configurar `Jwt:PreviousSigningKey` com a chave antiga durante a transição.

### Entrada de dados
- Validação nos DTOs (DataAnnotations) e regras de domínio (nomes: Unicode NFC, espaços colapsados, sem caracteres de controle ou invisíveis).
- Telefone normalizado para E.164 (libphonenumber); só celulares e fixos válidos.
- **Fotos de avatar** (ADR-0005): o arquivo enviado nunca é guardado nem repassado. O tipo é decidido pelo **conteúdo** (não pelo nome nem pelo `Content-Type`) e só JPEG, PNG, WebP e GIF passam; SVG, HTML e demais formatos são recusados. O servidor lê o cabeçalho **antes** de decodificar (lado máximo 8000 px, 25 megapixels, contra imagens "bomba"), decodifica, orienta, recorta, reduz e **reencoda** em WebP: tudo que estava escondido no original (EXIF/GPS, XMP, perfis, *polyglots*) é descartado. Limites: 3 MB por foto (`413` antes de ler o corpo quando o `Content-Length` já excede), 2 decodificações simultâneas (a hospedagem gratuita tem pouca memória) e 20 envios/hora por pessoa. A imagem é servida com `nosniff`, `Content-Security-Policy: default-src 'none'; sandbox` e tipo fixo `image/webp`.
- **Corpo das requisições limitado a 1 MB** em todo o servidor (Kestrel; acima disso, `413 request.too_large`). Só o envio de foto sobe o próprio limite, para 3 MB, e continua checado antes de ler o corpo. Os JSON da API são minúsculos perto disso, então o teto global só serve para barrar abuso.
- O cabeçalho `Server` não é enviado (não diz qual servidor é).
- Erros nunca vazam exceções: `ProblemDetails` com `code` estável e `traceId`; 500 genérico para o inesperado.

### Abuso
- Limite global por IP (600/min) e políticas próprias: login (30/min), cadastro (10/h), renovação (60/min), envio de foto (20/h **por pessoa**), exportação dos dados pessoais (5/h por pessoa), criação de grupo (10/h), tentativa de entrar em grupo (10/min), criação de partida (30/h) e ação de jogo (240/min), todos por pessoa. IP real via `X-Forwarded-For` (o app só é alcançável pelo proxy da hospedagem).
- **Cloudflare Turnstile** em login e cadastro, ligado quando `Turnstile:SecretKey` está definida; **falha fechada** se o Cloudflare não responder.
- **Válvula de escape:** `Registration:Mode=closed` bloqueia novos cadastros sem afetar quem já tem conta.

### Navegador e transporte
- CORS por lista explícita de origens (nunca `*`), com padrões regex para previews. `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `X-Frame-Options: DENY`, `Cache-Control: no-store` (exceto onde o endpoint define o seu), HSTS fora de Development.
- Tokens vão em `Authorization: Bearer` (sem cookies, logo sem CSRF). O WebSocket do SignalR aceita o token por `access_token` na query, **só** em `/hubs`.

### Banco de dados
- Tabelas no schema `app`; `public` vazio. A Data API (PostgREST) do Supabase **não expõe** `app` (verificado), e **RLS está ligado sem políticas** em todas as tabelas (teste automatizado).
- Acesso só pelo pooler do Supabase; o backend é o único cliente do banco.
- Evolução planejada: papéis separados (migrador × aplicação) com privilégios mínimos.

### Logs e privacidade
- Logs estruturados sem telefone, nome, senhas ou tokens; só ids e códigos. Corpo das requisições não é registrado.
- Retenção: a trilha de eventos de partidas encerradas é apagada após 60 dias e sessões de login expiradas ou encerradas, após 30 (limpeza automática, ADR-0009); o resultado e o placar das partidas ficam.
- Exclusão de conta (LGPD): anonimização; o hash do telefone é substituído por um valor aleatório. Os vínculos com grupos são encerrados (as linhas ficam, apontando para "Jogador removido", para o histórico continuar íntegro); quem é dono de grupo com outras pessoas precisa transferir antes.
- Acesso e portabilidade (LGPD): `GET /users/me/export` devolve uma cópia dos dados da própria pessoa (e só dela; o telefone nunca é guardado em claro, então consta apenas o final). O inventário completo dos dados, a base legal e a retenção estão em [LGPD.md](LGPD.md).

### Cadeia de suprimentos
- Dependabot (NuGet, Actions, Docker), auditoria do NuGet, `gitleaks` no CI, *secret scanning* e *push protection* do GitHub.
- Evitar pacotes com licença comercial (MediatR, AutoMapper, FluentAssertions 8+, ImageSharp 4+, que exige chave de licença até para compilar). As fotos de avatar usam **SkiaSharp** (MIT).
- O SkiaSharp traz decodificadores nativos (libjpeg-turbo, libpng, libwebp, giflib): é superfície de ataque conhecida. Mitigações: validação do cabeçalho e limites antes de decodificar, nada do original é repassado, e o Dependabot mantém o pacote (e seus binários nativos) atualizado: **atualizações de segurança do SkiaSharp devem ser aplicadas sem demora**.

## Como reportar uma vulnerabilidade

Não abra uma issue pública com detalhes exploráveis. Avise os mantenedores diretamente (conta GitHub dos donos do repositório) com passos para reproduzir. Se houver vazamento de segredo, **troque o segredo primeiro** e avise em seguida: o histórico do Git é permanente.

## Checklist para PRs que tocam em segurança

- [ ] A identidade vem do token? Nada é lido de telefone/id enviado pelo cliente.
- [ ] O novo endpoint exige autenticação (padrão) ou tem `[AllowAnonymous]` justificado?
- [ ] Quem pode ver/alterar? Há teste de autorização (outro usuário, fora do grupo)?
- [ ] Há informação secreta na resposta? Foi projetada por jogador?
- [ ] Entrada validada e com limite de tamanho? Há limite de taxa se for sensível?
- [ ] Nada de dado pessoal em logs, erros ou testes? Nenhum segredo no diff?
