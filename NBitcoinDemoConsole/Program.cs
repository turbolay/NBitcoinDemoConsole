using NBitcoin;
using NBitcoin.RPC;

namespace NBitcoinDemoConsole
{
	internal static class Program
	{
		private static Random rnd = new();
		private static Network network = Network.RegTest;
		private static Settings settings = new Settings("test", "test", 18444);

		private static void Main(string[] args)
		{
			Console.WriteLine($"Connecting to {network.Name}");

			var rpcClient = SetupRpcClient(settings, network);

			var mnemonicBigWallet = new Mnemonic(Wordlist.English, WordCount.Twelve); // Generate a 12-word mnemonic phrase
			ExtKey mKBigWallet = mnemonicBigWallet.DeriveExtKey();
			int counterDerivationBigWallet = 1;

			Console.WriteLine($"Mnemonic: {string.Join(" ", mnemonicBigWallet.Words)}");

			var mnemonicMinerWallet = new Mnemonic(Wordlist.English, WordCount.Twelve); // Generate a 12-word mnemonic phrase
			ExtKey mKMinerWallet = mnemonicMinerWallet.DeriveExtKey();
			int counterDerivationMinerWallet = 1;
			(var minerWalletAddressesSecrets, counterDerivationMinerWallet) = GetManyBitcoinAddressesSecretsPair(mKMinerWallet, counterDerivationMinerWallet, 10);

			// Fund miners for initial transactions
			List<Coin> minerInitialOutpointsTxOut = rpcClient.FundManyAddresses(minerWalletAddressesSecrets.Keys);

			// Make coinbase spendable
			rpcClient.GenerateToAddress(101, minerWalletAddressesSecrets.First().Key);

			var builder = network.CreateTransactionBuilder();
			builder.AddCoins(minerInitialOutpointsTxOut);
			builder.AddKeys(minerWalletAddressesSecrets.Values.Select(x => x.PrivateKey).ToArray());

			// Add outputs
			(var bigWalletAddressesSecrets, counterDerivationBigWallet) = GetManyBitcoinAddressesSecretsPair(mKBigWallet, counterDerivationBigWallet, rnd.Next(2, 8));
			var sumFunds = minerInitialOutpointsTxOut.Sum(x => x.TxOut.Value.Satoshi);
			var fundsForEachOutput = (sumFunds - 10000) / (minerWalletAddressesSecrets.Count() + bigWalletAddressesSecrets.Count());
			foreach (var destinationAddress in bigWalletAddressesSecrets.Keys.Concat(minerWalletAddressesSecrets.Keys))
			{
				builder.Send(destinationAddress, fundsForEachOutput);
			}

			builder.SendFees(10000);
			builder.SetChange(minerWalletAddressesSecrets.First().Key);
			var transaction = builder.BuildTransaction(true);

			var response = rpcClient.SendRawTransaction(transaction);
			Console.WriteLine($"Transaction sent: {response}");
		}

		private static RPCClient SetupRpcClient(Settings settings, Network network, string? walletName = null)
		{
			var credentialString = RPCCredentialString.Parse($"{settings.RpcUserName}:{settings.RpcPassword}");

			if (walletName is not null)
			{
				credentialString.WalletName = walletName;
			}

			return new RPCClient(credentialString, $"localhost:{settings.Port}", network);
		}

		private static List<Coin> FundManyAddresses(this RPCClient client, IEnumerable<BitcoinAddress> addresses)
		{
			List<Coin> result = new();
			foreach (var address in addresses)
			{
				var blockHash = client.GenerateToAddress(1, address);
				var bestBlock = client.GetBlock(blockHash.First());

				if (bestBlock.Transactions.Count > 0)
				{
					var coinbaseTx = bestBlock.Transactions[0]; // Assuming the first transaction is the coinbase
					var coinbaseOutput = coinbaseTx.Outputs[0]; // Assuming you want the second output of the coinbase

					var coinbaseOutPoint = new OutPoint(coinbaseTx.GetHash(), 0); // Assuming you want the second output
					var newCoin = new Coin(coinbaseOutPoint, coinbaseOutput);

					result.Add(newCoin);
				}
			}

			return result;
		}

		private static (Dictionary<BitcoinAddress, BitcoinSecret>, int) GetManyBitcoinAddressesSecretsPair(ExtKey mK, int indexStart, int number)
		{
			var listExtKeys = new Dictionary<BitcoinAddress, BitcoinSecret>();
			foreach (uint index in Enumerable.Range(indexStart, number))
			{
				var keyPath = new KeyPath("84'/0'/0'/1'/" + index + "'");
				var extKey = mK.Derive(keyPath);
				var address = extKey.ScriptPubKey.GetDestinationAddress(network);
				if (address is not null)
				{
					listExtKeys.Add(address, extKey.PrivateKey.GetBitcoinSecret(network));
				}
			}

			return (listExtKeys, number);
		}

		private record Settings(string RpcUserName, string RpcPassword, int Port);
	}
}