// A robust helper to create Deposit Instructions
// Handles: ATA Creation + Token-2022 Compatibility
public static async Task<List<TransactionInstruction>> CreateDepositTx(
    double amount, 
    string mintAddress, 
    string targetWallet)
{
    var instructions = new List<TransactionInstruction>();
    var walletPub = Web3.Account.PublicKey;
    var targetPub = new PublicKey(targetWallet);
    var mintPub = new PublicKey(mintAddress);

    // 1. DETECT: Is this a Token-2022 or Legacy Token?
    var mintInfo = await Web3.Rpc.GetAccountInfoAsync(mintPub);
    PublicKey tokenProgramId = TokenProgram.ProgramIdKey; // Default
    int decimals = 9;

    if (mintInfo.WasSuccessful && mintInfo.Result.Value != null) {
        tokenProgramId = new PublicKey(mintInfo.Result.Value.Owner);
        decimals = mintInfo.Result.Value.Decimals; // Get actual decimals
    }

    // 2. DERIVE: Find Associated Token Addresses (ATA)
    PublicKey sourceATA = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(walletPub, mintPub);
    PublicKey destATA = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(targetPub, mintPub);

    // 3. CHECK: Does Destination ATA exist?
    var destInfo = await Web3.Rpc.GetAccountInfoAsync(destATA);
    if (!destInfo.WasSuccessful || destInfo.Result.Value == null)
    {
        // NO? -> Create it first! (Payer = User)
        instructions.Add(AssociatedTokenAccountProgram.CreateAssociatedTokenAccount(
            walletPub, destATA, targetPub, mintPub, tokenProgramId
        ));
    }

    // 4. TRANSFER: Build the correct instruction
    ulong amountRaw = (ulong)(amount * System.Math.Pow(10, decimals));

    if (tokenProgramId.Equals(TokenProgram.ProgramIdKey))
    {
        // Standard SPL Transfer
        instructions.Add(TokenProgram.Transfer(sourceATA, destATA, amountRaw, walletPub));
    }
    else
    {
        // Token-2022 Transfer (Manual OpCode 3)
        var data = new byte[9];
        data[0] = 3;
        System.BitConverter.GetBytes(amountRaw).CopyTo(data, 1);
        
        instructions.Add(new TransactionInstruction {
            ProgramId = tokenProgramId,
            Keys = new List<AccountMeta> {
                AccountMeta.Writable(sourceATA, false),
                AccountMeta.Writable(destATA, false),
                AccountMeta.ReadOnly(walletPub, true)
            },
            Data = data
        });
    }

    return instructions;
}